using Zenith.Core.Navigation;
using Zenith.Core.Permissions;
using Zenith.Core.Registry;

namespace Zenith.Core.Tests.Registry;

public sealed class RegistryProposalTests
{
    [Fact]
    public void ProposalFreezesSelectionDomainsAndTheirReasonsWithoutExpansion()
    {
        var reasons = new[] { "Selected application." };
        var domains = new[] { new ProposedDomain("MAIL.EXAMPLE.", reasons) };
        var selection = new ServiceSelection("mail", "catalog-1");
        var proposal = new AccessProposal(selection, domains);
        reasons[0] = "Changed.";
        domains[0] = new("other.example", ["Changed."]);

        Assert.Same(selection, proposal.SelectedService);
        Assert.Equal("catalog-1", proposal.RegistryRevision);
        Assert.Equal("mail.example", Assert.Single(proposal.ProposedDomains).Hostname);
        Assert.Equal("Selected application.", Assert.Single(proposal.ProposedDomains[0].Reasons));
        Assert.Throws<NotSupportedException>(() => ((IList<ProposedDomain>)proposal.ProposedDomains).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)proposal.ProposedDomains[0].Reasons).Clear());
        Assert.Empty(new AccessProposal(selection, []).ProposedDomains);
    }

    [Fact]
    public void InvalidProposalMetadataIsRejectedWithoutConsultingPolicy()
    {
        var selection = new ServiceSelection("mail", "1");
        Assert.Throws<ArgumentException>(() => new ServiceSelection("Bad ID", "1"));
        Assert.Throws<ArgumentException>(() => new ServiceSelection("mail", " "));
        Assert.Throws<ArgumentException>(() => new ProposedDomain("https://mail.example/path", ["Application."]));
        Assert.Throws<ArgumentException>(() => new ProposedDomain("*.example", ["Application."]));
        Assert.Throws<ArgumentException>(() => new ProposedDomain("mail.example", []));
        Assert.Throws<ArgumentException>(() => new ProposedDomain("mail.example", [" "]));
        Assert.Throws<ArgumentException>(() => new AccessProposal(selection,
            [new("MAIL.EXAMPLE.", ["First."]), new("mail.example", ["Second."])]));
        Assert.Throws<ArgumentNullException>(() => new AccessProposal(null!, []));
        Assert.Throws<ArgumentException>(() => new AccessProposal(selection, [null!]));
    }

    [Fact]
    public void DescriptiveProposalCanNameDeniedHostsButCannotAuthorizeThem()
    {
        var source = new FixedSitePolicySource(new SitePolicySnapshot([
            new("allowed.example", AccessClass.Whitelist), new("blocked.example", AccessClass.Blacklist)]));
        var evaluator = new SitePolicyNavigationEvaluator(source);
        NavigationDecision Decide(string host) => evaluator.Evaluate(new($"https://{host}", NavigationOrigin.AddressBar));
        var before = new[] { Decide("allowed.example"), Decide("blocked.example"), Decide("login.example") };
        var provenance = new RelationshipProvenance(RegistrySourceType.OfficialDocumentation, "Documentation reference",
            new("Curator", DateTimeOffset.UnixEpoch), "Sign-in relationship.");
        var registry = new ServiceRegistry("1", [new("mail", "Mail", "communication", "Mail.", [],
            [new("blocked.example", "Application", true, DependencyType.Navigation, provenance)],
            [new("identity", provenance)], ServiceStatus.Initial)], [],
            [new("identity", "Identity", "Sign-in", [new("login.example", "Sign-in", true, DependencyType.Authentication, provenance)])]);
        var proposal = new AccessProposal(new("mail", registry.Revision),
            [new("blocked.example", ["Application."]), new("login.example", ["Sign-in."])]);

        Assert.Equal(2, proposal.ProposedDomains.Count);
        Assert.Equal(2, registry.GetDomainRequirements("mail").Count);
        Assert.Equal(before, new[] { Decide("allowed.example"), Decide("blocked.example"), Decide("login.example") });
        Assert.Equal(NavigationDenialReason.Blacklisted, Assert.IsType<NavigationDecision.Denied>(Decide("blocked.example")).Reason);
        Assert.Equal(NavigationDenialReason.Greylisted, Assert.IsType<NavigationDecision.Denied>(Decide("login.example")).Reason);
        Assert.False(new BrowserCapabilityPolicy().Evaluate(BrowserCapability.FileSelection).Allowed);
    }

    [Fact]
    public void RelationshipValidationRejectsInvalidMetadataAndDuplicateInfrastructure()
    {
        Assert.Throws<ArgumentException>(() => new RelationshipProvenance((RegistrySourceType)99));
        Assert.Throws<ArgumentException>(() => new RelationshipProvenance(RegistrySourceType.Curated, " "));
        Assert.Throws<ArgumentException>(() => new RelationshipReview(" ", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new RelationshipReview("Curator", DateTimeOffset.MinValue));
        Assert.Throws<ArgumentException>(() => new DomainRequirement("mail.example", "Mail", true, (DependencyType)99));
        Assert.Throws<ArgumentException>(() => new InfrastructureDependency("Bad ID"));
        Assert.Throws<ArgumentException>(() => new ServiceDefinition("mail", "Mail", "communication", "Mail", [],
            [new("mail.example", "Mail", true)], [new("identity"), new("identity", new(RegistrySourceType.Curated))], ServiceStatus.Initial));
    }
}
