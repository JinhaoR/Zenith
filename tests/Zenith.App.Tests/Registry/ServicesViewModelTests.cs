using Zenith.App.Registry;
using Zenith.Core.Registry;

namespace Zenith.App.Tests.Registry;

public sealed class ServicesViewModelTests
{
    [Fact]
    public async Task BundledCatalogGroupsCategoriesAndShowsExperimentalStatusWithoutApproval()
    {
        var model = new ServicesViewModel(() => new JsonServiceRegistryLoader().LoadBundledAsync());
        Assert.Empty(model.Categories);
        await model.EnsureLoadedAsync();
        Assert.True(model.IsAvailable);
        Assert.Equal(40, model.Categories.Sum(c => c.Services.Count));
        Assert.Equal(14, model.Categories.Single(c => c.Id == "research").Services.Count);
        Assert.Equal("AI", model.Categories.Single(c => c.Id == "ai").Name);
        var youtube = model.Categories.Single(c => c.Id == "testing").Services.Single();
        Assert.True(youtube.IsExperimental);
        Assert.Contains("Experimental", youtube.DisplayName);
        Assert.All(model.Categories.SelectMany(c => c.Services), s => Assert.Equal("Available in Registry", s.MembershipLabel));
        var gmail = model.Categories.SelectMany(c => c.Services).Single(s => s.Id == "gmail");
        Assert.Equal("Google email web application.", gmail.Description);
        Assert.Equal(new[] { "Email", "Authentication" }, gmail.Capabilities.Select(c => c.Name));
        Assert.False(gmail.IsExperimental);
        Assert.Equal("navigation", gmail.Domains[0].Classification);
        Assert.Equal("authentication", gmail.Infrastructure[0].Domains[0].Classification);
        Assert.Equal("api", gmail.Infrastructure[0].Domains[1].Classification);
        var unreviewed = model.Categories.SelectMany(c => c.Services).Single(s => s.Id == "claude");
        Assert.Equal("unknown", unreviewed.Domains[0].Classification);
        Assert.Equal("No provenance recorded.", unreviewed.Domains[0].Evidence);
    }

    [Theory]
    [InlineData("gMaIl", "gmail")]
    [InlineData(" GitHub ", "github")]
    [InlineData("arXiv", "arxiv")]
    public async Task FilteringAndSelectionAreLocalPresentationOnly(string query, string expectedId)
    {
        var calls = 0;
        var model = new ServicesViewModel(() => { calls++; return new JsonServiceRegistryLoader().LoadBundledAsync(); });
        await model.EnsureLoadedAsync();
        model.SearchText = query;
        var service = Assert.Single(Assert.Single(model.Categories).Services);
        Assert.Equal(expectedId, service.Id);
        model.SelectedService = service;
        Assert.Same(service, model.SelectedService);
        model.SearchText = "https://outside.example/";
        Assert.Empty(model.Categories);
        Assert.Null(model.SelectedService);
        model.SearchText = "communication";
        Assert.Equal(6, Assert.Single(model.Categories).Services.Count);
        await model.EnsureLoadedAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SharedInfrastructureKeepsEdgeEvidenceAndRequiredOptionalDomainsDistinct()
    {
        var edge = new RelationshipProvenance(RegistrySourceType.Curated, "edge note", explanation: "Service sign-in relationship.");
        var domain = new RelationshipProvenance(RegistrySourceType.OfficialDocumentation, "https://identity.example/docs",
            new("Test curator", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)), "Documented identity endpoint.");
        var requirements = new[] {
            new DomainRequirement("login.example", "Sign-in document", true, DependencyType.Authentication, domain),
            new DomainRequirement("api.example", "Optional API", false, DependencyType.Api)
        };
        ServiceDefinition Service(string id, RelationshipProvenance? evidence) => new(id, id, "communication", "Description", [],
            [new("mail.example", "Primary application", true, DependencyType.Navigation)], [new("identity", evidence)], ServiceStatus.Initial);
        var catalog = new ServiceRegistry("fixture-1", [Service("first", edge), Service("second", null)], [],
            [new("identity", "Identity", "Shared sign-in", requirements)]);
        var model = new ServicesViewModel(() => Task.FromResult(RegistryLoadResult.Success(catalog)));
        await model.EnsureLoadedAsync();
        var services = Assert.Single(model.Categories).Services;
        var first = Assert.Single(services[0].Infrastructure);
        var second = Assert.Single(services[1].Infrastructure);
        Assert.Equal(first.Id, second.Id);
        Assert.Same(edge, first.RelationshipEvidence);
        Assert.Null(second.RelationshipEvidence);
        Assert.Contains("edge note", first.Evidence);
        Assert.Equal("No provenance recorded.", second.Evidence);
        Assert.Same(requirements[0], first.Domains[0].Requirement);
        Assert.Same(requirements[1], second.Domains[1].Requirement);
        Assert.Equal("authentication", first.Domains[0].Classification);
        Assert.Equal("api", first.Domains[1].Classification);
        Assert.Equal("Required: yes (catalog description)", first.Domains[0].RequiredLabel);
        Assert.Equal("Required: no (optional)", first.Domains[1].RequiredLabel);
        Assert.Contains("Test curator", first.Domains[0].Evidence);
        Assert.Contains("2026-01-02T00:00:00.0000000+00:00", first.Domains[0].Evidence);
        Assert.Contains("Documented identity endpoint.", first.Domains[0].Evidence);
        Assert.Contains("https://identity.example/docs", first.Domains[0].Evidence);
        Assert.Equal("fixture-1", model.RegistryRevision);
        Assert.Throws<NotSupportedException>(() => ((IList<DomainPresentation>)first.Domains).Clear());
    }

    [Theory]
    [InlineData(DependencyType.Unknown, "unknown")]
    [InlineData(DependencyType.Navigation, "navigation")]
    [InlineData(DependencyType.Authentication, "authentication")]
    [InlineData(DependencyType.Api, "api")]
    [InlineData(DependencyType.EmbeddedResource, "embedded_resource")]
    public void DomainClassificationIsPresentedWithoutInferringPermissions(DependencyType type, string expected)
    {
        var requirement = new DomainRequirement("app.example", "Purpose unchanged", false, type);
        var presentation = new DomainPresentation(requirement);
        Assert.Same(requirement, presentation.Requirement);
        Assert.Equal(expected, presentation.Classification);
        Assert.Equal(requirement.Purpose, presentation.Purpose);
        Assert.Equal(requirement.Hostname, presentation.Hostname);
    }

    [Fact]
    public async Task FailureAndRetryNeverExposeAPartialOrStaleCatalog()
    {
        var good = await new JsonServiceRegistryLoader().LoadBundledAsync();
        RegistryLoadResult result = RegistryLoadResult.Failure([new("registry.json", "Invalid catalog.")]);
        var model = new ServicesViewModel(() => Task.FromResult(result));
        await model.EnsureLoadedAsync();
        Assert.False(model.IsAvailable);
        Assert.True(model.CanRetry);
        Assert.Empty(model.Categories);
        Assert.Contains("registry.json: Invalid catalog.", model.Diagnostics);
        result = good;
        await model.LoadAsync();
        Assert.True(model.IsAvailable);
        Assert.False(model.HasDiagnostics);
        model.SelectedService = model.Categories[0].Services[0];
        result = RegistryLoadResult.Failure([new("services/mail.json", "Missing file.")]);
        await model.LoadAsync();
        Assert.False(model.IsAvailable);
        Assert.Empty(model.Categories);
        Assert.Null(model.SelectedService);
        Assert.Empty(model.RegistryRevision);
    }

    [Fact]
    public async Task UnexpectedLoaderFailureIsContainedWithoutDisplayingExceptionDetails()
    {
        var model = new ServicesViewModel(() => throw new IOException("Private local path"));
        await model.EnsureLoadedAsync();
        Assert.False(model.IsLoading);
        Assert.False(model.IsAvailable);
        Assert.True(model.CanRetry);
        Assert.DoesNotContain("Private", model.Diagnostics);
        Assert.Empty(model.Categories);
    }

    [Fact]
    public async Task OpeningTwiceDoesNotStartConcurrentLoads()
    {
        var pending = new TaskCompletionSource<RegistryLoadResult>();
        var calls = 0;
        var model = new ServicesViewModel(() => { calls++; return pending.Task; });
        var loading = model.EnsureLoadedAsync();
        Assert.True(model.IsLoading);
        Assert.Empty(model.Categories);
        await model.EnsureLoadedAsync();
        await model.LoadAsync();
        Assert.Equal(1, calls);
        pending.SetResult(RegistryLoadResult.Failure([]));
        await loading;
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task OnlyReviewedServicesCanPrepareAndPolicyConflictsPreventContinuing()
    {
        var catalog = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
        var builder = new ServiceAccessProposalBuilder();
        Assert.Equal(new[] { "arxiv", "github", "gmail" }, catalog.Services.Where(s => builder.CanOffer(catalog, s.Id)).Select(s => s.Id).Order());
        var model = new ServicesViewModel(() => Task.FromResult(RegistryLoadResult.Success(catalog)));
        var continueCalls = 0;
        model.ConfigureProposals(() => new Zenith.Core.Navigation.SitePolicySnapshot([
            new("mail.google.com", Zenith.Core.Navigation.AccessClass.Blacklist)]), _ => continueCalls++);
        await model.EnsureLoadedAsync();
        model.SearchText = "Gmail";
        model.SelectedService = model.Categories[0].Services[0];
        model.PrepareProposal();
        Assert.False(model.CanContinue);
        Assert.Contains("Blacklist", model.ProposalSummary);
        model.ContinueToVault();
        Assert.Equal(0, continueCalls);
        model.SearchText = "Claude";
        model.SelectedService = model.Categories[0].Services[0];
        Assert.False(model.CanPrepareSelectedService);
        Assert.Null(model.Proposal);
        Assert.False(model.HasProposal);
    }

    [Fact]
    public async Task OptionalChoicesRebuildTheFrozenReviewAndUnavailablePolicyClearsIt()
    {
        var evidence = new RelationshipProvenance(RegistrySourceType.Curated, "Test source",
            new("Curator", DateTimeOffset.UnixEpoch), "Reviewed navigation.");
        var catalog = new ServiceRegistry("1", [new("mail", "Mail", "communication", "Mail", [], [
            new("mail.example", "Application", true, DependencyType.Navigation, evidence, PermissionApplicability.RequiredNavigation),
            new("optional.example", "Optional feature", false, DependencyType.Navigation, evidence, PermissionApplicability.OptionalNavigation)],
            [], ServiceStatus.Initial)], [], []);
        var available = true;
        AccessProposal? transferred = null;
        var model = new ServicesViewModel(() => Task.FromResult(RegistryLoadResult.Success(catalog)));
        model.ConfigureProposals(() => available ? new Zenith.Core.Navigation.SitePolicySnapshot([]) : null, p => transferred = p);
        await model.EnsureLoadedAsync();
        model.SelectedService = model.Categories[0].Services[0];
        model.PrepareProposal();
        Assert.False(Assert.Single(model.OptionalScopes).IsSelected);
        Assert.Single(model.Proposal!.ProposedDomains);
        var originalId = model.Proposal.ProposalId;
        model.OptionalScopes[0].IsSelected = true;
        model.PrepareProposal(model.OptionalScopes.Where(o => o.IsSelected).Select(o => o.Hostname).ToArray());
        Assert.Equal(2, model.Proposal!.ProposedDomains.Count);
        Assert.NotEqual(originalId, model.Proposal.ProposalId);
        model.ContinueToVault();
        Assert.Same(model.Proposal, transferred);
        available = false;
        model.PrepareProposal();
        Assert.Null(model.Proposal);
        Assert.False(model.CanContinue);
        Assert.NotEmpty(model.ProposalError);
    }
}
