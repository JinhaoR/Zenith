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

        var host = string.IsNullOrWhiteSpace(edit.AddHost) ? null : edit.AddHost.Trim();
        if (host is not null)
        {
            if (!SiteIdentity.TryCreate(host, out var identity))
            {
                throw new ArgumentException("Enter a hostname only, without a path, port or credentials.");
            }

            host = identity.Host;
            if (state.Sites.Length >= 1000)
            {
                throw new ArgumentException("The site limit has been reached.");
            }
            if (state.ToPolicy().Classify(identity) == AccessClass.Blacklist)
            {
                throw new ArgumentException("A Blacklisted destination cannot be added to your Sphere.");
            }
            if (state.Sites.Any(site => site.Host == host && site.AccessClass == AccessClass.Whitelist))
            {
                throw new ArgumentException("That hostname already has a Sphere entry.");
            }
            if (edit.IncludeSubdomains && Uri.CheckHostName(host) != UriHostNameType.Dns)
            {
                throw new ArgumentException("IP addresses cannot include subdomains.");
            }
        }

        if (host is null && settings == state.Settings && !edit.ChangePassword)
        {
            throw new ArgumentException("Choose at least one change before creating a proposal.");
        }

        return edit with { AddHost = host, IncludeSubdomains = host is not null && edit.IncludeSubdomains };
    }

    public static VaultSettings ApplySettings(VaultSettings current, VaultEdit edit) => new(
        edit.GreylistSeconds ?? current.GreylistSeconds,
        edit.GrantSeconds ?? current.GrantSeconds,
        edit.VaultSeconds ?? current.VaultSeconds);
}
