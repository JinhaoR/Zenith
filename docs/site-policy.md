# Site Policy

## 1. Scope

This document defines how Zenith classifies sites, evaluates navigation, issues Access Grants and applies Policy Changes.

## 2. Classification

Every site resolves to exactly one effective class:

- **Whitelist:** navigation is permitted directly.
- **Blacklist:** navigation is denied.
- **Greylist:** navigation is denied unless a valid Access Grant exists.

Unclassified sites are Greylisted by default. If policy data conflicts, Blacklist takes precedence over Whitelist. Ambiguous or invalid policy fails closed.

Site matching must use normalized URIs and structured host comparison. Substring matching is forbidden. Exact host, subdomain and wildcard semantics must be specified before their implementation; they must never be inferred ad hoc by UI code.

## Site Identity

Defines the unit that receives an Access Class.

Examples:
- registrable domain
- hostname
- origin
- URL path

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
