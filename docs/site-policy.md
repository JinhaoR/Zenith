# Site Policy

## 1. Scope

This document defines how Zenith classifies sites, evaluates navigation, issues Access Grants and applies Policy Changes.

## 1.1 Development Policy and Migration

Before initial password setup, the development application exposes a starter Site Policy snapshot for browser-mechanics testing. Its Whitelist entries are GitHub, ChatGPT, OpenAI, YouTube, Wikipedia, Reddit, Microsoft Learn, Google, Stack Overflow, GitLab, MDN Web Docs and Internet Archive. It has no Blacklist entries. Password setup persists these entries as the initial Vault policy. Later changes come only from confirmed Vault proposals.

The general evaluator directly permits HTTP(S) targets whose normalized hostname matches an active Whitelist entry under its explicit subdomain scope. Other valid sites resolve to Greylist and require the Access Grant procedure below. Unsupported targets fail closed. Lookalike hosts do not inherit another site's classification.

Starter catalog launch URLs must match their configured entry using the same host-scope matcher as navigation. A `www.` or other subdomain launch URL is valid only when that entry includes subdomains; accepting it does not change the entry's identity or scope.

Navigation reads the active immutable snapshot through `ISitePolicySource` for every decision. The Vault service supplies a validated, revisioned snapshot from protected storage. If initialized policy cannot be read or validated, all navigation is denied as policy unavailable; the starter snapshot is not a recovery fallback. Confirming a Policy Change atomically advances the revision.

Version-1 data migrates to the current version-4 envelope, retaining the existing password and pending Greylist requests. As explicitly requested for development testing, fresh setup and version-1 migration initialize all three timing values below to five seconds. Version-2 and version-3 migrations preserve existing Vault scopes, settings and pending proposal identities and deadlines. Data is validated before migration is written. Later launches never reset configured timings or replace saved policy with the starter catalog.

## 2. Classification

The mandatory synchronized Blacklist is an immutable overlay separate from Vault entries. Its fixed variant includes StevenBlack unified hosts, fake-news, gambling and porn, but not social media. Source selection and protection cannot be disabled or edited through the Vault. A canonical exact-host match wins over any Whitelist entry or Access Grant, including embedded content. See `adblocking.md` for synchronization and resource enforcement.

Vault review, staging and confirmation reject additions currently on the mandatory list. A later list update may override an existing Whitelist entry without deleting it. Upstream corrections/removals arrive through validated updates: permanent protection means a fixed, always-enabled source, not a union of every hostname ever listed. Without a valid mandatory list, active browsing policy is unavailable rather than reverting to starter-only access.

Every site resolves to exactly one effective class:

- **Whitelist:** navigation is permitted directly.
- **Blacklist:** navigation is denied.
- **Greylist:** navigation is denied unless a valid Access Grant exists.

Unclassified sites are Greylisted by default. If policy data conflicts, Blacklist takes precedence over Whitelist. Ambiguous or invalid policy fails closed.

Site matching must use normalized URIs and structured host comparison. Substring matching is forbidden. Exact host, subdomain and wildcard semantics must be specified before their implementation; they must never be inferred ad hoc by UI code.

## Site Identity

The unit that receives an Access Class is a normalized hostname.

- Only absolute HTTP and HTTPS navigation targets are eligible for Site Policy evaluation.
- Explicitly typed website addresses may omit the scheme: Core input preparation supplies HTTPS before ordinary policy evaluation. It never adds or strips `www.`, changes site identity, or retries another hostname. Engine links and redirects retain strict absolute-URL validation; explicit HTTP stays HTTP and there is no automatic HTTP downgrade.
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

### Embedded content compatibility decision

For the current development design, the user explicitly permits embedded frames as functionality of a top-level Whitelisted page. Such frames do not need their own Whitelist membership or Greylist challenge merely because their HTTP(S) hostname differs. This is embedded-content permission, not a change to the embedded site's Access Class or an Access Grant.

Direct navigation, top-level redirects, popups and new tabs continue to evaluate the destination independently. Embedded content does not enter Sphere discovery or become eligible for bookmarks through this exception. Website capability restrictions remain independent. Existing Blacklist precedence is not relaxed by this decision.

This compatibility choice applies to Whitelisted top-level pages; it does not authorize broadening a Greylisted page's temporary grant. For intercepted network documents, Core rechecks the top-level page and destination: otherwise-Greylisted frames are permitted under a currently Whitelisted top-level page, but a temporary Greylist page requires independent authorization for the embedded destination. An expired or unavailable top-level authorization denies further intercepted frame documents. This does not automatically terminate every previously opened connection.

### Network-document enforcement checkpoint

The account-security checkpoint also disables HTTP cache use and bypasses service-worker responses for each tab before document gating is ready. This mitigation does not prevent all worker execution. Certificate errors are cancelled with no exception route. Login redirects receive the same independent hostname decisions as other redirects; signing into a service never authorizes its parent, sibling or identity-provider hosts automatically. Explicit HTTP retains its documented support, with native identity information warning against entering credentials there.

Each tab installs a request-stage document gate before becoming ready, in addition to the native navigation checks. It uses host-side CDP frame IDs to distinguish main requests from frames and re-evaluates intercepted redirect hops through Core. Initialization failure leaves browsing unavailable; a runtime protection-channel failure closes the browser window and disposes its controllers. There is no unrestricted fallback.

Loopback-server regressions verify absence of denied HTTP redirect/script requests, including in new tabs, while allowed redirects and nested widgets still load. Inherited documents, cache/worker paths, out-of-process target coverage and connection lifetime remain Phase 5 work. See ADR 0012 for the evidence, failure behavior and limits; this is not a guarantee of zero DNS or connection activity.

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

Cooldown state must not be bypassed by reopening the URL or restarting Zenith. The following rules include the user-authorized short development timings; these are not release-ready minimums.

### Current grant and authentication rules

- One password is configured during initial setup and used for both challenges. Setup requires confirmation and 15–128 characters; setup itself does not start a cooldown or issue access.
- A successful first challenge captures the current Greylist wait and visit duration for the normalized hostname. The development defaults are **5 seconds of waiting** and **5 seconds of access**. Both are Vault-editable between 5 seconds and 24 hours. Reopening another URL on that hostname reuses the same pending request and cannot skip or restart its wait.
- After eligibility, a successful second challenge issues the captured visit duration. Access never starts automatically when the wait ends. Existing requests keep their captured deadline and duration when settings change; migration preserves the earlier 30-minute wait and 60-minute visit values for legacy requests.
- The grant covers the **exact normalized hostname across tabs**. Scheme, port and path follow the existing site-identity rules; parent domains and subdomains are not included implicitly. Redirects to another hostname receive their own policy decision.
- Cooldowns persist across restarts, including time spent with Zenith closed. Grants are held only in memory and end at expiry or Zenith exit. The second challenge consumes its pending request before issuing access, so reopening Zenith cannot reuse a completed wait to recreate that grant.
- Failed password attempts introduce a persisted five-second retry delay. Neither failed authentication nor failed persistence can advance the access procedure.
- Password and timing changes require the protected Vault procedure below. Forgotten-password recovery is not implemented and has no immediate reset route. Missing or corrupt initialized protected data does not offer fresh setup.
- A confirmed Vault revision invalidates existing session grants on the next authorization check. Pending Greylist requests remain saved, and any later challenge uses the then-current password.

Core checks grants with an inclusive start and exclusive expiry. Each navigation rechecks classification, the exact hostname, protected access-state health and time. Blacklist and unsupported-target decisions take precedence; a grant-source failure denies navigation. A grant never changes the site's Access Class or adds it to Sphere discovery/bookmarks.

Temporary access settings obtain a read-only snapshot of active session grants from Core. Only unexpired grants for currently Greylisted hosts are listed; unavailable policy or access state yields no active entries. Listing grants does not extend, persist or issue access.

App rechecks retained documents once per second and before activating a tab. Expired or unavailable authorization causes the external document to be hidden and unloaded. An already active document is cleared on the next host timer tick; tab activation must not briefly redisplay expired content.

### Time and failure behavior

During a session, Core compares UTC with monotonic elapsed time and checkpoints a persisted UTC high-water mark. A discrepancy exceeding five seconds, or rollback more than five seconds below the saved mark, disables temporal authorization for that session. Correct the system clock and restart to revalidate saved waits. Clock anomalies do not reclassify sites; readable Whitelist policy remains usable. Unreadable durable Vault policy denies all navigation, while an access-only failure cannot create a grant.

Offline progress uses UTC deadlines. Without trusted external time, changes made while Zenith is closed and complete local-state rollback cannot be reliably detected. This development limitation is explicit in `docs/threat-model.md`; the storage and clock design is recorded in ADR 0007.

## 5. Policy Changes

An access-affecting Vault edit is a Policy Change, not an immediate mutation.

1. The user authenticates and proposes the exact change.
2. Zenith records the proposal and the policy revision on which it is based.
3. A long waiting period begins.
4. The existing policy remains active.
5. After the waiting period, the user re-authenticates and confirms the unchanged proposal.
6. Zenith applies the Policy Change atomically.

Changing a pending proposal restarts its waiting period. An unconfirmed, expired or conflicting proposal does not take effect. Cancellation may remove a pending proposal without weakening the active policy.

### Current Vault rules

- The development Vault wait defaults to **5 seconds**, adjustable between 5 seconds and 30 days. These short values are explicitly for testing, not an authentication bypass. All changes, including stricter ones, follow the full process; there is no immediate-apply exception.
- One pending proposal can combine a password replacement, timing edits and multiple hostname additions, scope updates and removals (at most 1,000 site operations). Removals are evaluated first, then additions, and the resulting policy applies atomically. This permits replacing a broad parent scope with selected services in one proposal. Duplicate or invalid selections reject the whole proposal. Staging authenticates with the current shared password and records a unique proposal ID, the current policy revision and an eligibility deadline computed using the **old, active Vault wait**.
- Reducing the Vault wait cannot shorten its own proposal. Replacing a proposal starts the full currently active wait again, with a new ID. Cancellation immediately discards only the pending proposal; it never modifies active policy or credentials.
- Confirmation must name the unchanged pending proposal and authenticate with the current password after eligibility. A stale, conflicting, cancelled or already-consumed proposal cannot apply. Eligible proposals remain pending until explicit confirmation, replacement or cancellation; they do not auto-apply.
- Password replacements require 15–128 characters and repeated entry. Only a salted verifier is staged. The old password remains active until confirmation atomically replaces the credential, policy revision and pending state. A failed write cannot partially change credentials or policy. This procedure requires knowledge of the current password and is not recovery.
- New Vault additions default to the exact selected hostname. Including subdomains is an explicit per-service option. The hostname is always the boundary: `scholar.google.com`, even with subdomains included, never permits `google.com`, `www.google.com` or `mail.google.com`. A broad `google.com` entry does permit those children, so it must be removed when replacing it with selected services. Zenith never infers a registrable parent, a `www.` alias, unrelated domains or permission for login redirects. Embedded-content compatibility remains separate. Review provides expandable normalized addresses and scopes; friendly display names are metadata only.
- Existing Whitelist scopes may be expanded or narrowed only through the full staged Policy Change. A hostname already covered by a broader Whitelist entry is rejected as redundant unless the broader scope is removed in the same batch. Confirming a new broader parent entry consolidates redundant Whitelist children; overlapping Blacklist entries remain intact and continue to win. Narrowing an existing entry does not delete separately stored child entries. Invalid hosts, credentials, paths and wildcard strings are rejected. Existing saved scopes are never narrowed automatically by this UI change.
- A removal must name an independent stored Whitelist scope. It remains active throughout review and waiting. Confirming removal of a host-and-subdomains scope atomically removes that Whitelist entry and its covered redundant Whitelist children. It never removes or weakens overlapping Blacklist entries. A child still covered by a broader parent cannot be removed independently because that would be an ineffective policy change; the broader scope must be removed instead.
- Confirmed additions immediately appear in the Whitelist-backed Sphere directory and become eligible for bookmarks. Confirmed removals disappear immediately; their existing bookmarks no longer appear as accessible shortcuts, and retained pages are denied on the next policy validation. A pending proposal never affects discovery or navigation.
- The store permits one supported writer at a time and validates initialized state. It retains a protected previous envelope for future controlled recovery but never silently restores an older policy or password after corruption. Current local-user/offline-time limitations remain documented in the threat model and ADR 0008.

## 6. Required Guarantees

- Website content cannot create an Access Grant or Policy Change.
- A blocked-navigation surface cannot reclassify a site.
- An Access Grant cannot become permanent implicitly.
- Clock rollback must not shorten a cooldown or Policy Change delay.
- Every decision should expose a reason suitable for user-facing explanation and diagnostics.
