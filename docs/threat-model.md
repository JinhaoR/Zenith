# Threat Model

## 1. Security Goal

Zenith must enforce the user's previously chosen access policy consistently, including when the user experiences a later impulse to weaken it.

Zenith protects the integrity of:

- Site classification.
- Vault policy.
- Pending Policy Changes.
- Greylist cooldowns.
- Access Grants.
- Authentication material.

## 2. Relevant Threats

Threats include:

- A website attempting to escape its classification.
- A navigation path bypassing the central evaluator.
- Redirects, popups, embedded frames or external schemes reaching restricted content.
- Deceptive hostnames, subdomains, internationalized domains or malformed URLs.
- Restarting Zenith or altering the system clock to shorten a delay.
- Corrupt, missing or partially written policy state.
- A filter-list update that is unavailable, malformed or compromised.
- Sensitive values appearing in logs or diagnostics.
- Web content attempting to invoke privileged host functionality.
- External content continuing to run after a native Sphere or policy-boundary surface visually replaces it.

## 3. Trust Boundaries

- WebView2 and all rendered web content are untrusted with respect to Zenith policy.
- Zenith.Core is the authority for access decisions.
- The Vault is the authority for durable policy.
- External lists are untrusted input until validated.
- The system clock alone must not be trusted to prove that a delay elapsed.

## 4. Required Mitigations

- Route every navigation mechanism through common policy logic.
- Unload external documents replaced by native Zenith surfaces. A host-initiated internal clear may admit only its exact internal target and must not become a general navigation exception.
- Evaluate policy before allowing content to load.
- Normalize and compare URIs structurally.
- Persist cooldowns and Policy Changes safely. Access Grants are intentionally session-only under the current Site Policy; restarting must never recreate a consumed grant.
- Apply policy writes atomically and retain a recoverable last-known-good state.
- Fail closed when policy or authentication cannot be evaluated.
- Never store or log plaintext passwords.
- Validate external list formats and retain the last valid version after update failure.
- Test bypass paths as well as normal behavior.

## 5. Scope Limit

Zenith is not initially a hardened kiosk or operating-system access-control system. A user with administrative control of Windows may install another browser, modify application files or remove Zenith.

The initial promise is narrower: while using Zenith in its supported environment, Zenith must not undermine its own policy through ordinary browsing behavior or implementation inconsistencies.

Stronger tamper resistance may be considered later and must be documented as a separate security goal.

## 6. Current Development Boundary

The account-security checkpoint rejects server-certificate exceptions without a bypass, clears cached certificate decisions and resets old renderer permissions before browsing. New password saving and general autofill are disabled; old saved passwords require confirmed browser-data cleanup to remove. A native identity command displays the normalized engine origin and distinguishes HTTP from HTTPS, not trustworthy from malicious sites. Cookies and website sessions may persist in WebView2's profile; Vault credential protection must not be mistaken for encryption of all browser data. SmartScreen is requested and remains subject to Windows settings. Runtime-update notifications request a restart; they do not establish update-policy health or update Zenith itself.

Confirmed browsing-data cleanup disposes live tabs, clears the renderer profile through WebView2's supported API and closes Zenith. It never deletes Vault state, saved waits or Zenith bookmarks, does not revoke server-side sessions and is not secure erasure. Failure closes browsing without claiming complete deletion. The application adapters do not log request bodies, authentication headers or raw runtime exceptions. See ADR 0014 for implementation and verification scope.

Document interception now bypasses service-worker responses and disables cache use per tab. Workers can still register or perform background tasks; this does not complete worker/network isolation or the existing-connection threat model. Sensitive-account readiness still requires broader integration testing and independent security review.

Ad-resource filtering uses a bundled maintained engine in a host-side JavaScript
context, with no exposed CLR objects. It is not an OS process sandbox. Fixed
subscription downloads are bounded and compiled before protected atomic
publication; failures do not modify site policy. The cosmetic message bridge has
no policy services and treats page messages as bounded untrusted DOM hints,
scoped using WebView2's actual sender URL. Scriptlets, custom style actions and
downloaded executable replacements are excluded. A page can still tamper with
its own cosmetic presentation; CSS is not a security boundary. Exact request
frame attribution and comprehensive cache/worker coverage remain limitations.
See ADR 0011 and `adblocking.md` for the implemented controls and remaining risks.

Mandatory host protection is outside Vault-editable policy. Startup without a validated source/cache denies browsing; failed updates retain the current list. Exact-host matches override Whitelist and grants, including intercepted frame/resource requests. The host snapshot is bounded and structurally validated, protected by DPAPI on disk, and replaced atomically. HTTPS and a fixed GitHub source are the supply-chain trust boundary; the displayed SHA-256 identifies downloaded content but is not an upstream signature. Source compromise, complete offline rollback, same-user code execution and undiscovered domains remain outside these guarantees. No local exception mechanism is provided. See ADR 0010 for limits and testing.

Temporary access and the Vault use Windows user-scoped protection for credentials, cooldowns, policy and pending proposals, atomic state replacement, an initialization marker and a single-writer lifetime lease. Missing or corrupt initialized durable policy denies navigation; the UI offers no credential reset. DPAPI protects stored data but does not defend against arbitrary code running as the same Windows user. Removing the entire security directory while Zenith is closed is also outside this checkpoint's tamper resistance; the marker is a missing/partial-state defense, not an undeletable installation identity.

Password changes stage only salted verifiers and require the old password at confirmation. Credential replacement, policy revision and proposal consumption are one atomic write. Shared transaction locking prevents supported Vault/Greylist operations from interleaving across authentication and commit. Mandatory persisted fields, proposal IDs and base revisions are validated. The previous protected envelope is retained for a future controlled-recovery design; it is never silently restored, since doing so could revive an old password or weaker policy.

The user explicitly requested five-second timing defaults and development minimums for testing. These values exercise the same gates and persistence as longer waits, but provide very little intentional delay. They are not production-strength friction; release defaults/minimums still require a separate decision. Increasing or decreasing a value is itself a staged Policy Change and never an immediate testing toggle.

During a running session, UTC is checked against monotonic elapsed time; a saved high-water mark also detects material rollback on restart. Detected anomalies revoke session grants and require clock correction and restart before revalidation. Offline time necessarily relies on UTC deadlines in this development design. Advancing the clock while Zenith is closed, or restoring an entire older local security snapshot, is not reliably detectable without trusted external time/anti-rollback storage. Stronger guarantees remain an explicit hardening investigation, not a claim of this checkpoint.

Core rechecks every implemented top-level navigation. App rechecks tab activation and retained pages on a one-second host timer, then hides/unloads revoked documents. This is not yet comprehensive frame, download, service-worker, permission or network-resource enforcement; those retain their later roadmap scope. See ADR 0007 and the permission/filtering documents before extending those paths.

Redirect regressions distinguish cancelled page navigation from network-request prevention. A loopback server reproduced denied HTTP redirects and script navigations reaching the server before native cancellation. A request-stage CDP document gate now stops those reproduced requests, using native frame identity and Core policy before resuming intercepted documents. Failed initialization leaves tabs unavailable; runtime channel failure closes the window and disposes controllers. Tests verify both denied-request absence and permitted embedded functionality. This does not establish universal zero network contact: DNS, speculative connections, cache/worker paths, out-of-process targets and existing connections remain open hardening work. See ADR 0012. Permission/download/external-launch hooks are implemented as described in the permission-model checkpoint; other capability paths remain incomplete.
