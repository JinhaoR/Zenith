using Zenith.Core.Navigation;
using Zenith.Core.Permissions;
using Zenith.Core.Registry;

namespace Zenith.Core.Tests.Registry;

public sealed class ServiceRegistryTests
{
    private static readonly CapabilityDefinition Email = new("email", "Email", "Service email function.");
    private static InfrastructureDefinition Identity() => new("identity", "Identity", "Shared sign-in.", [new("login.example", "Sign-in", true)]);
    private static ServiceDefinition Service(string id, string host = "shared.example", string[]? dependencies = null) =>
        new(id, id, "communication", "Catalog entry.", ["email"], [new(host, "Application", true)],
            (dependencies ?? ["identity"]).Select(id => new InfrastructureDependency(id)), ServiceStatus.Initial);
    private static ServiceRegistry Catalog() => new("1", [Service("first"), Service("second")], [Email], [Identity()]);

    [Fact]
    public void QueriesKeepSharedPrimaryAndInfrastructureRelationships()
    {
        var registry = Catalog();
        Assert.Equal(2, registry.ListServices().Count);
        Assert.Equal(2, registry.ListServicesByCategory("communication").Count);
        Assert.Empty(registry.ListServicesByCategory("unknown"));
        Assert.Equal(2, registry.ListServicesByCapability("email").Count);
        Assert.Equal(new[] { "first", "second" }, registry.FindServicesAssociatedWithHostname("SHARED.example.").Select(s => s.Id));
        Assert.Equal(2, registry.FindServicesAssociatedWithHostname("LOGIN.example").Count);
        Assert.Equal("identity", Assert.Single(registry.GetInfrastructureDependencies("first")).Id);
        var requirements = registry.GetDomainRequirements("first");
        Assert.Null(requirements[0].InfrastructureId);
        Assert.Equal("identity", requirements[1].InfrastructureId);
        Assert.All(requirements, r => Assert.Equal("first", r.ServiceId));
        Assert.Null(registry.FindService("missing"));
        Assert.Empty(registry.GetInfrastructureDependencies("missing"));
        Assert.Empty(registry.GetDomainRequirements("missing"));
    }

    [Theory]
    [InlineData("www.shared.example")]
    [InlineData("notshared.example")]
    [InlineData("shared.example.evil.test")]
    [InlineData("https://shared.example/path")]
    [InlineData("unknown.example")]
    public void HostLookupDoesNotInferScopeOrRelationships(string host) =>
        Assert.Empty(Catalog().FindServicesAssociatedWithHostname(host));

    [Fact]
    public void ModelAndSnapshotCollectionsCannotBeChangedThroughInputOrOutput()
    {
        var domains = new[] { new DomainRequirement("APP.EXAMPLE.", "Application", false) };
        var capabilities = new[] { "email" };
        var dependencies = new[] { new InfrastructureDependency("identity") };
        var service = new ServiceDefinition("mail", "Mail", "communication", "Mail service.", capabilities, domains, dependencies, ServiceStatus.Initial);
        var services = new[] { service };
        var registry = new ServiceRegistry("1", services, [Email], [Identity()]);
        domains[0] = new("changed.example", "Changed", true);
        capabilities[0] = "changed";
        dependencies[0] = new("changed");
        services[0] = Service("other");
        Assert.Equal("app.example", service.Domains[0].Hostname);
        Assert.False(service.Domains[0].Required);
        Assert.Equal("email", service.Capabilities[0]);
        Assert.Equal("identity", service.InfrastructureDependencies[0].InfrastructureId);
        Assert.Same(service, registry.FindService("mail"));
        Assert.Throws<NotSupportedException>(() => ((IList<ServiceDefinition>)registry.Services)[0] = Service("other"));
        Assert.Throws<NotSupportedException>(() => ((IList<DomainRequirement>)service.Domains).Clear());
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("example.com:443")]
    [InlineData("*.example.com")]
    [InlineData("user@example.com")]
    [InlineData(" example.com")]
    [InlineData("")]
    public void InvalidHostsCannotEnterModels(string host) =>
        Assert.Throws<ArgumentException>(() => new DomainRequirement(host, "Application", true));

    [Fact]
    public void NormalizationReusesCoreIdentity()
    {
        Assert.True(SiteIdentity.TryCreate("BÜCHER.example.", out var identity));
        Assert.Equal(identity.Host, new DomainRequirement("BÜCHER.example.", "Application", true).Hostname);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("capability")]
    [InlineData("infrastructure")]
    public void DuplicateIdsPreventSnapshotCreation(string kind)
    {
        var services = kind == "service" ? new[] { Service("mail"), Service("mail") } : [Service("mail")];
        var capabilities = kind == "capability" ? new[] { Email, Email } : [Email];
        var infrastructure = kind == "infrastructure" ? new[] { Identity(), Identity() } : [Identity()];
        Assert.Contains(RegistryValidator.Validate(services, capabilities, infrastructure), i => i.Message.Contains("Duplicate ID"));
        Assert.Throws<ArgumentException>(() => new ServiceRegistry("1", services, capabilities, infrastructure));
    }

    [Fact]
    public void UnresolvedReferencesAreReportedTogether()
    {
        var issues = RegistryValidator.Validate([Service("mail")], [], []);
        Assert.Contains(issues, i => i.Message.Contains("Unknown capability"));
        Assert.Contains(issues, i => i.Message.Contains("Unknown infrastructure"));
    }

    [Fact]
    public void DuplicateCanonicalDomainsAreRejectedWithinDefinitions()
    {
        var domains = new[] { new DomainRequirement("EXAMPLE.com", "Main", true), new DomainRequirement("example.com.", "Duplicate", false) };
        var service = new ServiceDefinition("mail", "Mail", "communication", "Mail.", [], domains, [], ServiceStatus.Initial);
        var infrastructure = new InfrastructureDefinition("identity", "Identity", "Sign-in.", domains);
        Assert.Equal(2, RegistryValidator.Validate([service], [], [infrastructure]).Count);
        Assert.Throws<ArgumentException>(() => new ServiceRegistry("1", [service], [], [infrastructure]));
    }

    [Fact]
    public void EmptyDomainsMissingTextInvalidIdsAndStatusAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityDefinition("email", "", "Description"));
        Assert.Throws<ArgumentException>(() => new CapabilityDefinition("Bad ID", "Email", "Description"));
        Assert.Throws<ArgumentException>(() => new ServiceDefinition("mail", "Mail", "communication", "Mail.", [], [], [], (ServiceStatus)99));
        var empty = new InfrastructureDefinition("identity", "Identity", "Sign-in.", []);
        Assert.Contains(RegistryValidator.Validate([], [], [empty]), i => i.Message.Contains("At least one domain"));
    }

    [Fact]
    public void CatalogRelationshipsNeverGrantNavigationOrNativeCapabilities()
    {
        var source = new FixedSitePolicySource(new SitePolicySnapshot([new("blocked.example", AccessClass.Blacklist)]));
        var evaluator = new SitePolicyNavigationEvaluator(source);
        var registry = new ServiceRegistry("1", [Service("mail", "blocked.example")], [Email], [Identity()]);
        Assert.Single(registry.FindServicesAssociatedWithHostname("blocked.example"));
        Assert.Equal(NavigationDenialReason.Blacklisted, Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(new("https://blocked.example", NavigationOrigin.AddressBar))).Reason);
        Assert.Equal(NavigationDenialReason.Greylisted, Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(new("https://login.example", NavigationOrigin.AddressBar))).Reason);
        Assert.False(new BrowserCapabilityPolicy().Evaluate(BrowserCapability.FileSelection).Allowed);
    }
}
