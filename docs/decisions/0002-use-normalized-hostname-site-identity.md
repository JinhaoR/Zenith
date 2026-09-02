# ADR 0002: Use Normalized Hostnames for Site Identity

- **Status:** Accepted
- **Date:** 2026-09-02

## Context

Zenith must classify a navigation target before WebView2 may render it. Raw URL strings are unsuitable for policy matching because equivalent hosts can differ in case, Unicode representation, trailing dots and default-port spelling. Substring comparison also permits deceptive lookalike hosts.

The first Site Policy implementation needs a stable identity boundary before the durable Whitelist, Blacklist, Greylist and Vault models are built.

## Decision

Use the normalized hostname as a site's policy identity.

- Only absolute HTTP and HTTPS navigation targets can produce an identity.
- DNS names are converted to lowercase ASCII IDN form and trailing dots are removed.
- IP addresses are canonicalized and match only exactly.
- URL credentials are rejected.
- Scheme, port, path, query and fragment remain part of the navigation target but not the site identity.
- Including subdomains is an explicit property of a policy entry. It is not inferred by string containment.
- Subdomain checks operate on DNS-label boundaries.

The temporary starter Whitelist includes subdomains for each configured entry. Future durable policy data must preserve that scope explicitly.

## Alternatives Considered

- Registrable-domain identity using the Public Suffix List.
- Origin identity consisting of scheme, hostname and port.
- Full URL or path-prefix identity.
- Unnormalized string matching.

## Consequences

### Positive

- Policy behavior is deterministic and independently testable in Core.
- Lookalike hosts such as `github.com.evil.example` do not match `github.com`.
- Unicode and equivalent DNS spellings resolve to one identity.
- Individual entries can choose whether they include subdomains.

### Negative

- HTTP and HTTPS, and services on different ports, share one Access Class.
- Path-specific access rules are not represented.
- A broad entry with subdomains enabled can cover many independently operated services beneath that hostname.

If Zenith later requires origin-level or path-level rules, changing this identity boundary will require a new ADR and a policy-data migration.
