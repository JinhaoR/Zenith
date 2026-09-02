using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class SitePolicySnapshotTests
{
    [Fact]
    public void UnmatchedSitesAreGreylistedByDefault()
    {
        var policy = new SitePolicySnapshot([]);

        Assert.Equal(AccessClass.Greylist, policy.Classify(Identity("example.com")));
    }

    [Fact]
    public void ExactWhitelistEntryDoesNotIncludeSubdomains()
    {
        var policy = new SitePolicySnapshot(
        [
            new SitePolicyEntry("example.com", AccessClass.Whitelist)
        ]);

        Assert.Equal(AccessClass.Whitelist, policy.Classify(Identity("example.com")));
        Assert.Equal(AccessClass.Greylist, policy.Classify(Identity("www.example.com")));
    }

    [Fact]
    public void SubdomainScopeMatchesOnlyAtDnsLabelBoundaries()
    {
        var policy = new SitePolicySnapshot(
        [
            new SitePolicyEntry(
                "example.com",
                AccessClass.Whitelist,
                includeSubdomains: true)
        ]);

        Assert.Equal(AccessClass.Whitelist, policy.Classify(Identity("docs.example.com")));
        Assert.Equal(AccessClass.Greylist, policy.Classify(Identity("notexample.com")));
        Assert.Equal(AccessClass.Greylist, policy.Classify(Identity("example.com.evil.test")));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("private.example.com")]
    public void BlacklistTakesPrecedenceOverOverlappingWhitelist(string target)
    {
        var policy = new SitePolicySnapshot(
        [
            new SitePolicyEntry(
                "example.com",
                AccessClass.Whitelist,
                includeSubdomains: true),
            new SitePolicyEntry(
                "private.example.com",
                AccessClass.Blacklist,
                includeSubdomains: false),
            new SitePolicyEntry(
                "example.com",
                AccessClass.Blacklist,
                includeSubdomains: false)
        ]);

        Assert.Equal(AccessClass.Blacklist, policy.Classify(Identity(target)));
    }

    [Fact]
    public void PolicyCopiesItsInputEntries()
    {
        var entries = new List<SitePolicyEntry>
        {
            new("example.com", AccessClass.Whitelist)
        };
        var policy = new SitePolicySnapshot(entries);

        entries.Clear();

        Assert.Single(policy.Entries);
        Assert.Equal(AccessClass.Whitelist, policy.Classify(Identity("example.com")));
    }

    [Fact]
    public void PolicyPreservesItsRevision()
    {
        var policy = new SitePolicySnapshot([], revision: 7);

        Assert.Equal(7, policy.Revision);
    }

    [Fact]
    public void PolicyRejectsANegativeRevision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SitePolicySnapshot([], revision: -1));
    }

    [Fact]
    public void GreylistCannotBeStoredAsAnEntry()
    {
        Assert.Throws<ArgumentException>(() =>
            new SitePolicyEntry("example.com", AccessClass.Greylist));
    }

    [Fact]
    public void EntryRejectsAnUnknownAccessClass()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SitePolicyEntry("example.com", (AccessClass)99));
    }

    [Fact]
    public void EntryRejectsAnInvalidHost()
    {
        Assert.Throws<ArgumentException>(() =>
            new SitePolicyEntry("not a host", AccessClass.Whitelist));
    }

    [Fact]
    public void PolicyRejectsNullEntries()
    {
        Assert.Throws<ArgumentException>(() =>
            new SitePolicySnapshot([null!]));
    }

    private static SiteIdentity Identity(string host)
    {
        Assert.True(SiteIdentity.TryCreate(host, out var identity));
        return identity;
    }
}
