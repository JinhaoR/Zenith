# Site Policy

## 1. Scope

This document defines how Zenith classifies sites, evaluates navigation, issues Access Grants and applies Policy Changes.

## 1.1 Development Starter Policy

Until the durable Vault-backed policy is implemented, the development application uses an immutable Site Policy snapshot for browser-mechanics testing. Its Whitelist entries are GitHub, ChatGPT, OpenAI, YouTube, Wikipedia, Reddit, Microsoft Learn, Google, Stack Overflow, GitLab, MDN Web Docs and Internet Archive. It currently has no Blacklist entries.

The general evaluator permits only HTTP(S) targets whose normalized hostname matches one of those Whitelist entries under its explicit subdomain scope. Other valid sites resolve to Greylist and remain unavailable because Access Grants are not implemented yet. Unsupported targets fail closed. Lookalike hosts do not inherit another site's classification. The immutable development snapshot does not define the final persisted policy and must be replaced by a validated Vault-backed policy source before release.

Navigation reads the active immutable snapshot through `ISitePolicySource` for every decision. Snapshots carry a non-negative revision and validate their entries at construction. If the source cannot provide a trustworthy snapshot, or fails while doing so, navigation is denied as policy unavailable. The development source is fixed; a future Vault-backed App adapter may atomically replace the snapshot without changing Core classification behavior.

## 2. Classification

Every site resolves to exactly one effective class:

- **Whitelist:** navigation is permitted directly.
- **Blacklist:** navigation is denied.
- **Greylist:** navigation is denied unless a valid Access Grant exists.

Unclassified sites are Greylisted by default. If policy data conflicts, Blacklist takes precedence over Whitelist. Ambiguous or invalid policy fails closed.

Site matching must use normalized URIs and structured host comparison. Substring matching is forbidden. Exact host, subdomain and wildcard semantics must be specified before their implementation; they must never be inferred ad hoc by UI code.

## Site Identity

The unit that receives an Access Class is a normalized hostname.

- Only absolute HTTP and HTTPS navigation targets are eligible for Site Policy evaluation.
- DNS hostnames are represented in lowercase ASCII IDN form with any trailing dot removed.
- IP addresses are canonicalized and match only exactly.
- URL credentials are rejected rather than normalized.
- Scheme, port, path, query and fragment remain part of the requested navigation target but do not change site identity.
- A policy entry must state whether it includes subdomains. Subdomains are matched only at DNS-label boundaries.

For example, `www.github.com` is a subdomain of `github.com`, while `notgithub.com` and `github.com.evil.example` are unrelated identities. The development starter entries currently include their subdomains. The durable policy must preserve this scope explicitly rather than inferring it from interface behavior.

## 3. Navigation Evaluation

All top-level navigation paths use the same Core policy evaluator, including:

- Address-bar navigation.
- Links and redirects.
- New windows and popups.
- Restored sessions.
- URLs opened from other applications.
- External or custom URI schemes.

Policy is evaluated before navigation is permitted. Failure to load or evaluate policy does not produce unrestricted browsing.

## 4. Access Grants

A Greylisted site may receive an Access Grant only after this sequence:

1. The user requests access to the identified site.
2. The first password challenge succeeds.
3. The configured Greylist cooldown elapses.
4. The second password challenge succeeds.
5. Zenith issues an Access Grant with explicit scope and expiry.

An Access Grant:

- Applies only to its recorded scope.
- Must not silently include parent domains, subdomains or unrelated URLs.
- Must expire according to policy.
- Does not reclassify the site.
- Cannot override the Blacklist.

Cooldown state must not be bypassed by reopening the URL or restarting Zenith. Exact grant scope, duration and restart behavior remain explicit policy decisions and must be recorded before implementation.

## 5. Policy Changes

An access-affecting Vault edit is a Policy Change, not an immediate mutation.

1. The user authenticates and proposes the exact change.
2. Zenith records the proposal and the policy revision on which it is based.
3. A long waiting period begins.
4. The existing policy remains active.
5. After the waiting period, the user re-authenticates and confirms the unchanged proposal.
6. Zenith applies the Policy Change atomically.

Changing a pending proposal restarts its waiting period. An unconfirmed, expired or conflicting proposal does not take effect. Cancellation may remove a pending proposal without weakening the active policy.

The default duration and any exception for strictly restriction-increasing changes must be decided and documented before the Vault is implemented.

## 6. Required Guarantees

- Website content cannot create an Access Grant or Policy Change.
- A blocked-navigation surface cannot reclassify a site.
- An Access Grant cannot become permanent implicitly.
- Clock rollback must not shorten a cooldown or Policy Change delay.
- Every decision should expose a reason suitable for user-facing explanation and diagnostics.
