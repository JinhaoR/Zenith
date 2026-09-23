using System.Text.Json.Nodes;
using Zenith.App.Access;
using Zenith.App.Navigation;
using Zenith.App.Registry;
using Zenith.Core.Navigation;
using Zenith.Core.Registry;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Registry;

public sealed class JsonServiceRegistryLoaderTests
{
    [Fact]
    public async Task BundledCatalogContainsAllRequestedServicesAndDescriptiveRelationships()
    {
        var result = await new JsonServiceRegistryLoader().LoadBundledAsync();
        Assert.True(result.IsSuccess, string.Join("; ", result.Issues));
        Assert.Empty(result.Issues);
        var registry = Assert.IsType<ServiceRegistry>(result.Registry);
        var expected = "arxiv google_scholar semantic_scholar crossref orcid pubmed aps_journals nature science springerlink ieee_xplore inspire_hep jstor overleaf github gitlab stack_overflow stack_exchange mdn microsoft_learn docker_hub npm pypi nuget crates_io docs_rs gmail outlook proton_mail slack discord microsoft_teams google_drive onedrive dropbox notion google_calendar chatgpt claude youtube".Split(' ');
        Assert.Equal(expected.Order(), registry.Services.Select(s => s.Id).Order());
        Assert.Equal(14, registry.ListServicesByCategory("research").Count);
        Assert.Equal(ServiceStatus.Experimental, registry.FindService("youtube")!.Status);
        Assert.Equal(new[] { "python" }, registry.FindService("pypi")!.Ecosystems);
        Assert.Equal(new[] { "rust" }, registry.FindService("docs_rs")!.Ecosystems);
        Assert.Equal(new[] { "javascript" }, registry.FindService("mdn")!.Ecosystems);
        Assert.Equal(new[] { "gmail", "google_calendar", "google_drive" }, registry.FindServicesAssociatedWithHostname("ACCOUNTS.GOOGLE.COM.").Select(s => s.Id).Order());
        Assert.Equal("google_identity", Assert.Single(registry.GetInfrastructureDependencies("gmail")).Id);
        Assert.Equal(3, registry.GetDomainRequirements("gmail").Count);
        Assert.Empty(registry.FindServicesAssociatedWithHostname("www.mail.google.com"));
    }

    [Theory]
    [InlineData("json")]
    [InlineData("version")]
    [InlineData("missing_version")]
    [InlineData("missing_name")]
    [InlineData("missing_required")]
    [InlineData("null")]
    [InlineData("duplicate_property")]
    [InlineData("unknown_property")]
    [InlineData("duplicate_capability_id")]
    [InlineData("duplicate_infrastructure_id")]
    [InlineData("duplicate_service_id")]
    [InlineData("duplicate_domain")]
    [InlineData("missing_capability")]
    [InlineData("missing_infrastructure")]
    [InlineData("invalid_hostname")]
    [InlineData("invalid_status")]
    [InlineData("traversal")]
    [InlineData("duplicate_file")]
    [InlineData("missing_file")]
    [InlineData("oversized")]
    public async Task InvalidCatalogNeverExposesAPartialSnapshot(string damage)
    {
        using var fixture = new Fixture();
        var loader = new JsonServiceRegistryLoader();
        var good = await loader.LoadDirectoryAsync(fixture.Path);
        Assert.True(good.IsSuccess);
        switch (damage)
        {
            case "json": fixture.Write("services/mail.json", "{"); break;
            case "version": fixture.Edit("registry.json", o => o["schemaVersion"] = 2); break;
            case "missing_version": fixture.Edit("registry.json", o => o.AsObject().Remove("schemaVersion")); break;
            case "missing_name": fixture.Edit("services/mail.json", o => o.AsObject().Remove("name")); break;
            case "missing_required": fixture.Edit("services/mail.json", o => o["domains"]![0]!.AsObject().Remove("required")); break;
            case "null": fixture.Edit("services/mail.json", o => o["domains"] = null); break;
            case "duplicate_property": fixture.Write("services/mail.json", fixture.Read("services/mail.json").Replace("\"id\":\"mail\"", "\"id\":\"mail\",\"id\":\"mail\"")); break;
            case "unknown_property": fixture.Edit("services/mail.json", o => o["grantAccess"] = true); break;
            case "duplicate_capability_id": fixture.Edit("capabilities.json", o => o["capabilities"]!.AsArray().Add(o["capabilities"]![0]!.DeepClone())); break;
            case "duplicate_infrastructure_id": fixture.Edit("infrastructure.json", o => o["infrastructure"]!.AsArray().Add(o["infrastructure"]![0]!.DeepClone())); break;
            case "duplicate_service_id":
                fixture.Write("services/other.json", fixture.Read("services/mail.json"));
                fixture.Edit("registry.json", o => o["services"]!.AsArray().Add("services/other.json")); break;
            case "duplicate_domain": fixture.Edit("services/mail.json", o => o["domains"]!.AsArray().Add(o["domains"]![0]!.DeepClone())); break;
            case "missing_capability": fixture.Edit("services/mail.json", o => o["capabilities"]![0] = "unknown"); break;
            case "missing_infrastructure": fixture.Edit("services/mail.json", o => o["requires"]![0] = "unknown"); break;
            case "invalid_hostname": fixture.Edit("services/mail.json", o => o["domains"]![0]!["hostname"] = "https://mail.example/path"); break;
            case "invalid_status": fixture.Edit("services/mail.json", o => o["status"] = 17); break;
            case "traversal": fixture.Edit("registry.json", o => o["services"]![0] = "../mail.json"); break;
            case "duplicate_file": fixture.Edit("registry.json", o => o["services"]!.AsArray().Add("services/mail.json")); break;
            case "missing_file": File.Delete(System.IO.Path.Combine(fixture.Path, "infrastructure.json")); break;
            case "oversized": fixture.Write("services/mail.json", new string(' ', JsonServiceRegistryLoader.MaximumFileBytes + 1)); break;
        }
        var result = await loader.LoadDirectoryAsync(fixture.Path);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Registry);
        Assert.NotEmpty(result.Issues);
        Assert.NotNull(good.Registry!.FindService("mail"));
    }

    [Fact]
    public async Task CancellationReturnsFailureWithoutPartialCatalog()
    {
        using var fixture = new Fixture();
        var result = await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path, new CancellationToken(true));
        Assert.False(result.IsSuccess);
        Assert.Null(result.Registry);
        Assert.Contains(result.Issues, i => i.Message.Contains("cancelled"));
    }

    [Fact]
    public async Task RegistryFailureDoesNotAffectRealVaultPolicyStartupOrPersistence()
    {
        using var fixture = new Fixture();
        var accessPath = System.IO.Path.Combine(fixture.Path, "Access");
        using var store = new ProtectedAccessStore(accessPath);
        store.InitializeWithoutPassword(DateTimeOffset.UtcNow);
        var bytes = await File.ReadAllBytesAsync(System.IO.Path.Combine(accessPath, "access.bin"));
        fixture.Write("registry.json", "not JSON");
        Assert.False((await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path)).IsSuccess);
        var vault = new VaultService(store);
        Assert.True(vault.TryGetActivePolicy(out _));
        var navigation = new NavigationCoordinator(new SitePolicyNavigationEvaluator(vault));
        Assert.IsType<NavigationDecision.Allowed>(navigation.EvaluateAddressBarRequest("https://github.com"));
        Assert.Equal(NavigationDenialReason.Greylisted, Assert.IsType<NavigationDecision.Denied>(navigation.EvaluateAddressBarRequest("https://mail.proton.me")).Reason);
        var catalog = await new JsonServiceRegistryLoader().LoadBundledAsync();
        Assert.True(catalog.IsSuccess);
        Assert.NotEmpty(catalog.Registry!.FindServicesAssociatedWithHostname("mail.proton.me"));
        var proposal = new AccessProposal(new("proton_mail", catalog.Registry.Revision),
            [new("mail.proton.me", ["Descriptive service selection only."])]);
        Assert.Equal(catalog.Registry.Revision, proposal.RegistryRevision);
        Assert.Equal(NavigationDenialReason.Greylisted, Assert.IsType<NavigationDecision.Denied>(navigation.EvaluateAddressBarRequest("https://mail.proton.me")).Reason);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(System.IO.Path.Combine(accessPath, "access.bin")));
    }

    [Theory]
    [InlineData("navigation", DependencyType.Navigation)]
    [InlineData("authentication", DependencyType.Authentication)]
    [InlineData("api", DependencyType.Api)]
    [InlineData("embedded_resource", DependencyType.EmbeddedResource)]
    [InlineData("unknown", DependencyType.Unknown)]
    public async Task DependencyTypesAndRelationshipProvenanceSurviveLoading(string type, DependencyType expected)
    {
        using var fixture = new Fixture();
        const string evidence = """{"sourceType":"official_documentation","sourceReference":"https://example.test/documentation","review":{"reviewedBy":"Fixture curator","reviewedAt":"2026-01-02T12:00:00Z"},"explanation":"Fixture relationship evidence."}""";
        fixture.Edit("services/mail.json", o =>
        {
            o["domains"]![0]!["dependencyType"] = type;
            o["domains"]![0]!["provenance"] = JsonNode.Parse(evidence);
            o["requires"]![0] = JsonNode.Parse("""{"id":"identity"}""");
            o["requires"]![0]!["provenance"] = JsonNode.Parse(evidence);
        });
        fixture.Edit("infrastructure.json", o =>
        {
            o["infrastructure"]![0]!["domains"]![0]!["dependencyType"] = type;
            o["infrastructure"]![0]!["domains"]![0]!["provenance"] = JsonNode.Parse(evidence);
        });
        var loaded = await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path);
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Issues));
        var registry = loaded.Registry!;
        var associations = registry.GetDomainRequirements("mail");
        Assert.All(associations, a => Assert.Equal(expected, a.Domain.DependencyType));
        var provenance = registry.FindService("mail")!.InfrastructureDependencies[0].Provenance!;
        Assert.Equal(RegistrySourceType.OfficialDocumentation, provenance.SourceType);
        Assert.Equal("https://example.test/documentation", provenance.SourceReference);
        Assert.Equal("Fixture curator", provenance.Review!.ReviewedBy);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero), provenance.Review.ReviewedAt);
        Assert.Equal("Fixture relationship evidence.", provenance.Explanation);
        Assert.All(associations, a => Assert.Equal(provenance, a.Domain.Provenance));
        Assert.Null(associations[0].InfrastructureProvenance);
        Assert.Equal(provenance, associations[1].InfrastructureProvenance);
    }

    [Fact]
    public async Task ExistingCatalogDoesNotInventClassificationOrEvidence()
    {
        using var fixture = new Fixture();
        var result = await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path);
        Assert.True(result.IsSuccess);
        Assert.All(result.Registry!.GetDomainRequirements("mail"), a =>
        {
            Assert.Equal(DependencyType.Unknown, a.Domain.DependencyType);
            Assert.Null(a.Domain.Provenance);
            Assert.Null(a.InfrastructureProvenance);
        });
    }

    [Theory]
    [InlineData("curated", RegistrySourceType.Curated)]
    [InlineData("official_documentation", RegistrySourceType.OfficialDocumentation)]
    [InlineData("observed_dependency", RegistrySourceType.ObservedDependency)]
    [InlineData("user_added", RegistrySourceType.UserAdded)]
    [InlineData("community", RegistrySourceType.Community)]
    public async Task ProvenanceMayOmitReferenceReviewAndExplanation(string sourceType, RegistrySourceType expected)
    {
        using var fixture = new Fixture();
        fixture.Edit("services/mail.json", o => o["domains"]![0]!["provenance"] = new JsonObject { ["sourceType"] = sourceType });
        var result = await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path);
        Assert.True(result.IsSuccess);
        var provenance = result.Registry!.FindService("mail")!.Domains[0].Provenance!;
        Assert.Equal(expected, provenance.SourceType);
        Assert.Null(provenance.SourceReference);
        Assert.Null(provenance.Review);
        Assert.Null(provenance.Explanation);
    }

    [Theory]
    [InlineData("dependencyType", "\"invalid\"")]
    [InlineData("dependencyType", "1")]
    [InlineData("provenance", "{}")]
    [InlineData("provenance", "{\"sourceType\":\"invalid\"}")]
    [InlineData("provenance", "{\"sourceType\":1}")]
    [InlineData("provenance", "{\"sourceType\":\"curated\",\"confidence\":\"high\"}")]
    [InlineData("provenance", "{\"sourceType\":\"curated\",\"review\":{}}")]
    [InlineData("provenance", "{\"sourceType\":\"curated\",\"review\":{\"reviewedBy\":\"Curator\",\"reviewedAt\":\"invalid\"}}")]
    [InlineData("provenance", "{\"sourceType\":\"curated\",\"sourceReference\":\" \"}")]
    public async Task InvalidRelationshipMetadataRejectsWholeCatalog(string field, string json)
    {
        using var fixture = new Fixture();
        fixture.Edit("infrastructure.json", o => o["infrastructure"]![0]!["domains"]![0]![field] = JsonNode.Parse(json));
        var result = await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Registry);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("17")]
    [InlineData("{\"id\":\"identity\",\"grantAccess\":true}")]
    [InlineData("{\"id\":\"missing\"}")]
    [InlineData("{\"id\":\"identity\",\"provenance\":{}}")]
    public async Task InvalidInfrastructureRelationshipRejectsWholeCatalog(string json)
    {
        using var fixture = new Fixture();
        fixture.Edit("services/mail.json", o => o["requires"]![0] = JsonNode.Parse(json));
        var result = await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Registry);
    }

    [Fact]
    public async Task StringAndObjectCannotDuplicateTheSameInfrastructureRelationship()
    {
        using var fixture = new Fixture();
        fixture.Edit("services/mail.json", o => o["requires"]!.AsArray().Add(JsonNode.Parse("""{"id":"identity"}""")));
        var result = await new JsonServiceRegistryLoader().LoadDirectoryAsync(fixture.Path);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Registry);
        Assert.Contains(result.Issues, i => i.Message.Contains("duplicate references"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("Zenith-Registry-");
        internal string Path => _directory.FullName;
        internal Fixture()
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "services"));
            Write("registry.json", """{"schemaVersion":1,"revision":"fixture-1","dependencyCoverage":"primary_only","services":["services/mail.json"]}""");
            Write("capabilities.json", """{"capabilities":[{"id":"email","name":"Email","description":"Mail function."}]}""");
            Write("infrastructure.json", """{"infrastructure":[{"id":"identity","name":"Identity","description":"Sign-in.","domains":[{"hostname":"login.example","purpose":"Sign-in","required":true}]}]}""");
            Write("services/mail.json", """{"id":"mail","name":"Mail","category":"communication","description":"Mail service.","capabilities":["email"],"domains":[{"hostname":"mail.example","purpose":"Application","required":true}],"requires":["identity"],"status":"initial"}""");
        }
        internal string Read(string file) => File.ReadAllText(System.IO.Path.Combine(Path, file));
        internal void Write(string file, string contents) => File.WriteAllText(System.IO.Path.Combine(Path, file), contents);
        internal void Edit(string file, Action<JsonNode> edit)
        {
            var json = JsonNode.Parse(Read(file))!;
            edit(json);
            Write(file, json.ToJsonString());
        }
        public void Dispose() => _directory.Delete(true);
    }
}
