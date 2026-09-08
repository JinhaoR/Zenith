using Zenith.Core.Navigation;

namespace Zenith.Core.Vault;

// Shared by review, staging, confirmation and persisted-proposal validation.
// These rules do not authenticate, advance time or write state.
internal static class VaultProposalRules
{
    public static VaultEdit Normalize(VaultState state, VaultEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var settings = ApplySettings(state.Settings, edit);
        settings.Validate();

        if (edit.AddSites is not null || edit.RemoveSites is not null)
            return NormalizeBatch(state, edit);

        var host = string.IsNullOrWhiteSpace(edit.AddHost) ? null : edit.AddHost.Trim();
        var removeHost = string.IsNullOrWhiteSpace(edit.RemoveHost) ? null : edit.RemoveHost.Trim();
        if (host is not null && removeHost is not null)
        {
            throw new ArgumentException("Add or remove one Sphere site per proposal, not both.");
        }
        if (host is not null)
        {
            if (!SiteIdentity.TryCreate(host, out var identity))
            {
                throw new ArgumentException("Enter a hostname only, without a path, port or credentials.");
            }

            host = identity.Host;
            if (state.ToPolicy().Classify(identity) == AccessClass.Blacklist)
            {
                throw new ArgumentException("A Blacklisted destination cannot be added to your Sphere.");
            }

            var exact = state.Sites.FirstOrDefault(site =>
                site.Host == host && site.AccessClass == AccessClass.Whitelist);
            if (exact is not null && exact.IncludeSubdomains == edit.IncludeSubdomains)
            {
                throw new ArgumentException("That hostname already has a Sphere entry.");
            }
            if (exact is null && state.Sites.Any(site =>
                    site.AccessClass == AccessClass.Whitelist &&
                    site.IncludeSubdomains &&
                    SiteIdentity.TryCreate(site.Host, out var parent) &&
                    identity.IsSameOrSubdomainOf(parent)))
            {
                throw new ArgumentException("That hostname is already included by a broader Sphere entry.");
            }
            if (exact is null && state.Sites.Length >= 1000)
            {
                throw new ArgumentException("The site limit has been reached.");
            }
            if (edit.IncludeSubdomains && Uri.CheckHostName(host) != UriHostNameType.Dns)
            {
                throw new ArgumentException("IP addresses cannot include subdomains.");
            }
        }

        if (removeHost is not null)
        {
            if (!SiteIdentity.TryCreate(removeHost, out var removeIdentity))
            {
                throw new ArgumentException("Choose a valid Sphere hostname to remove.");
            }
            removeHost = removeIdentity.Host;
            var exact = state.Sites.FirstOrDefault(site =>
                site.Host == removeHost && site.AccessClass == AccessClass.Whitelist);
            if (exact is null)
            {
                throw new ArgumentException("That hostname does not have its own Sphere entry.");
            }
            if (state.Sites.Any(site =>
                    site.AccessClass == AccessClass.Whitelist &&
                    site.Host != removeHost &&
                    site.IncludeSubdomains &&
                    SiteIdentity.TryCreate(site.Host, out var parent) &&
                    removeIdentity.IsSameOrSubdomainOf(parent)))
            {
                throw new ArgumentException("That hostname remains included by a broader Sphere entry. Remove the broader entry instead.");
            }
        }

        if (host is null && removeHost is null && settings == state.Settings && !edit.ChangePassword)
        {
            throw new ArgumentException("Choose at least one change before creating a proposal.");
        }

        return edit with
        {
            AddHost = host,
            IncludeSubdomains = host is not null && edit.IncludeSubdomains,
            RemoveHost = removeHost
        };
    }

    public static VaultSettings ApplySettings(VaultSettings current, VaultEdit edit) => new(
        edit.GreylistSeconds ?? current.GreylistSeconds,
        edit.GrantSeconds ?? current.GrantSeconds,
        edit.VaultSeconds ?? current.VaultSeconds);

    private static VaultEdit NormalizeBatch(VaultState state, VaultEdit edit)
    {
        if (edit.AddSites is null || edit.RemoveSites is null || edit.AddHost is not null ||
            edit.RemoveHost is not null || edit.IncludeSubdomains ||
            edit.AddSites.Count + edit.RemoveSites.Count > 1000)
            throw new ArgumentException("Invalid site change selection.");

        var additions = new List<VaultSiteAddition>();
        var removals = new List<string>();
        var working = state with { Pending = null };
        foreach (var host in edit.RemoveSites)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Choose a site to remove.");
            var normalized = Normalize(state, new(RemoveHost: host));
            if (removals.Contains(normalized.RemoveHost!)) throw new ArgumentException("A site was selected for removal twice.");
            removals.Add(normalized.RemoveHost!);
        }
        working = working with { Sites = ApplySites(working, new(AddSites: [], RemoveSites: removals)) };
        foreach (var addition in edit.AddSites)
        {
            if (addition is null || string.IsNullOrWhiteSpace(addition.Host))
                throw new ArgumentException("Choose a site to add.");
            var name = addition.DisplayName?.Trim();
            if (name is not null && (name.Length is 0 or > 80 || name.Any(char.IsControl)))
                throw new ArgumentException("Use a site name of 1–80 characters.");
            var normalized = Normalize(working, new(AddHost: addition.Host, IncludeSubdomains: addition.IncludeSubdomains));
            if (additions.Any(item => item.Host == normalized.AddHost))
                throw new ArgumentException("A site was selected for addition twice.");
            var item = new VaultSiteAddition(normalized.AddHost!, normalized.IncludeSubdomains, name);
            additions.Add(item);
            working = working with { Sites = ApplySites(working, new(AddSites: [item], RemoveSites: [])) };
        }
        if (working.Sites.ToHashSet().SetEquals(state.Sites) &&
            ApplySettings(state.Settings, edit) == state.Settings && !edit.ChangePassword)
            throw new ArgumentException("Choose at least one change before creating a proposal.");
        // Copy the caller's collections so edits after review cannot change the proposal.
        return edit with { AddSites = additions.AsReadOnly(), RemoveSites = removals.AsReadOnly() };
    }

    public static VaultSite[] ApplySites(VaultState state, VaultEdit edit)
    {
        var sites = state.Sites.ToList();
        foreach (var host in edit.Removals())
        {
            var exact = state.Sites.First(site => site.Host == host && site.AccessClass == AccessClass.Whitelist);
            var scope = new SitePolicyEntry(host, AccessClass.Whitelist, exact.IncludeSubdomains);
            sites.RemoveAll(site => site.AccessClass == AccessClass.Whitelist &&
                SiteIdentity.TryCreate(site.Host, out var identity) && scope.Matches(identity));
        }
        foreach (var addition in edit.Additions())
        {
            var exact = sites.FindIndex(site => site.Host == addition.Host && site.AccessClass == AccessClass.Whitelist);
            if (exact >= 0) sites[exact] = sites[exact] with
            {
                IncludeSubdomains = addition.IncludeSubdomains,
                DisplayName = addition.DisplayName ?? sites[exact].DisplayName
            };
            else
            {
                var scope = new SitePolicyEntry(addition.Host, AccessClass.Whitelist, addition.IncludeSubdomains);
                if (addition.IncludeSubdomains)
                    sites.RemoveAll(site => site.AccessClass == AccessClass.Whitelist &&
                        SiteIdentity.TryCreate(site.Host, out var identity) && scope.Matches(identity));
                sites.Add(new(addition.Host, AccessClass.Whitelist, addition.IncludeSubdomains, addition.DisplayName));
            }
        }
        return sites.ToArray();
    }
}
