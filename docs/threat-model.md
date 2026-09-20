# Threat Model

## 1. Security Goal

Zenith must enforce the user's previously chosen access policy consistently, including when the user experiences a later impulse to weaken it.

Clarified 2026-09-12: Zenith is a browser-level intentional-access system, not a
general-purpose network firewall. Site classes govern visible website/document
access through navigation, redirects, popups and the applicable frame policy.
Ordinary background communication by an authorized website is not a violation
merely because its destination is not Whitelisted. Resource filtering supplies
additional protection within its documented coverage; it is not universal egress
isolation or a guarantee about the provenance of everything an allowed page displays.

Sensitive-account security is a separate requirement: preserve Chromium TLS,
certificate validation, sandboxing and origin isolation; prevent web content from
obtaining native privileges or policy authority; protect credentials and browser
state from unintended disclosure; and reliably remove documents when Zenith
claims they have been removed. Whitelist membership authorizes browsing, never
trust with WPF/Core privileges. Existing transport and capability restrictions
remain implemented; this clarification does not authorize removing them.

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
- Bundled uBO Lite is a trusted content-processing dependency with access to web
  pages, including authenticated content. It gains no WPF/Core policy authority
  or native bridge. Package verification does not sandbox extension background
  traffic or protect against a compromised upstream release. Its precise limits
  are documented in [browser-extensions.md](browser-extensions.md).

## 4. Required Mitigations

- Route every navigation mechanism through common policy logic.
- Unload external documents replaced by native Zenith surfaces. A host-initiated internal clear may admit only its exact internal target and must not become a general navigation exception. Keep the recorded document until successful matching blank navigation completes or the controller is destroyed. Failed/cancelled clearing, a ten-second dispatcher deadline, or process failure during clearing destroys the affected controller and discards deferred navigation. A destroyed controller cannot be reused; a new tab initializes the existing guards normally. Failure to destroy terminates Zenith. Hiding and best-effort suspension do not establish removal.
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

The follow-up connection acceptance run confirmed a denied WebSocket handshake
reaching a loopback receiver on WebView2 152.0.4191.66 (SEC-CONN-001). Ordinary
resource interception must not be assumed to cover WebSockets. The strict
connection gate currently fails; `connection-coverage.md` records the measured
scope, positive controls and untested transports. It was a release blocker under
the earlier zero-contact interpretation. Under the clarified
model, F01/SEC-CONN-001 is Informational: a request-filtering coverage limitation,
not a demonstrated navigation bypass, credential leak, TLS break or host escape.
It does not require a gateway/WFP remediation for primary-account readiness.
The historical strict runner still fails on this criterion; its result must be
interpreted using the current [account-security assessment](security-review.md).

ADR 0016 adds HTTPS-only public navigation and intercepted resources, with a literal-loopback development exception that never grants site access. Fixed Core transport/capability rules cannot be overridden by the Vault or filter exceptions. Native request-source identity replaces active-page assumptions for shared/service-worker network requests. Legacy service workers are terminated/unregistered before tabs become ready, and new intercepted service-worker requests are denied. Tests exposed persisted-worker traffic missing a newly installed listener, which is why request interception alone is insufficient. Startup before cleanup completes, speculative connections, WebSockets, out-of-process coverage and already-established connections are not proven isolated. A failed worker cleanup closes browsing rather than retaining potentially unguarded controllers.

Synthetic form/OTP and credential-bearing redirect regressions are browser-boundary tests, not real-provider MFA certification or OAuth protocol validation. Test the intended account/provider flow with disposable credentials and resolve the account-relevant findings in `security-review.md`. Independent review is recommended; its absence is not itself a vulnerability. Unrelated providers and exhaustive network-isolation tests are not account-readiness prerequisites. Do not treat this development checkpoint as approval for primary email use.

The follow-up account-security checkpoint (ADR 0015) closes the WPF favicon URL-loading path: tab icons are bounded PNG bytes supplied by WebView2, never website-controlled filesystem, UNC or network addresses handed to the native image loader. The native window caption shows the normalized engine origin rather than a site-controlled title. HTML file-input dialogs are cancelled and controller file drops disabled. Core filter exceptions deny requests; native adapter metadata/response failures produce local denials or close browsing if no response can be assigned. These measures reduce attack surface but do not constitute an independent security audit.

The account-security checkpoint rejects server-certificate exceptions without a bypass, clears cached certificate decisions and resets old renderer permissions before browsing. New password saving and general autofill are disabled; old saved passwords require confirmed browser-data cleanup to remove. A native identity command displays the normalized engine origin and distinguishes HTTP from HTTPS, not trustworthy from malicious sites. Cookies and website sessions may persist in WebView2's profile; Vault credential protection must not be mistaken for encryption of all browser data. SmartScreen is requested and remains subject to Windows settings. Runtime-update notifications request a restart; they do not establish update-policy health or update Zenith itself.

Confirmed browsing-data cleanup disposes live tabs, clears the renderer profile through WebView2's supported API and closes Zenith. It never deletes Vault state, saved waits or Zenith bookmarks, does not revoke server-side sessions and is not secure erasure. Failure closes browsing without claiming complete deletion. The application adapters do not log request bodies, authentication headers or raw runtime exceptions. See ADR 0014 for implementation and verification scope.

Document interception bypasses service-worker responses and disables cache use per tab. The worker restrictions above supersede the earlier registration behavior, but do not establish universal network isolation, which is outside the clarified product requirement. Sensitive-account readiness depends on the targeted lifecycle, origin, host, capability and profile checks in `security-review.md`, not banning ordinary background activity.

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

Password protection is optional and off by default, including a one-time migration of existing profiles explicitly requested on 2026-09-19. Cooldowns and explicit confirmations still protect intentional access. When protection is enabled, password changes require the old password at both stages. Enabling/replacing stages only salted verifiers; disabling clears the active verifier. Authentication mode, credential replacement, policy revision and proposal consumption are one atomic write. Shared transaction locking prevents supported Vault/Greylist operations from interleaving across authentication and commit. The version-5 password flag is mandatory: missing or invalid data cannot default to passwordless access. The previous protected envelope is retained for future controlled recovery and never silently restored; migration/disable is not secure erasure of prior backups. See ADR 0019.

The user explicitly requested five-second timing defaults and development minimums for testing. These values exercise the same gates and persistence as longer waits, but provide very little intentional delay. They are not production-strength friction; release defaults/minimums still require a separate decision. Increasing or decreasing a value is itself a staged Policy Change and never an immediate testing toggle.

During a running session, UTC is checked against monotonic elapsed time; a saved high-water mark also detects material rollback on restart. Detected anomalies revoke session grants and require clock correction and restart before revalidation. Offline time necessarily relies on UTC deadlines in this development design. Advancing the clock while Zenith is closed, or restoring an entire older local security snapshot, is not reliably detectable without trusted external time/anti-rollback storage. Stronger guarantees remain an explicit hardening investigation, not a claim of this checkpoint.

Core rechecks every implemented top-level navigation. App rechecks tab activation and retained pages on a one-second host timer, then hides/unloads revoked documents. This is not yet comprehensive frame, download, service-worker, permission or network-resource enforcement; those retain their later roadmap scope. See ADR 0007 and the permission/filtering documents before extending those paths.

Redirect regressions distinguish cancelled page navigation from network-request prevention. A loopback server reproduced denied HTTP redirects and script navigations reaching the server before native cancellation. A request-stage CDP document gate now stops those reproduced requests, using native frame identity and Core policy before resuming intercepted documents. Failed initialization leaves tabs unavailable; runtime channel failure closes the window and disposes controllers. Tests verify both denied-request absence and permitted embedded functionality. This does not establish universal zero network contact: DNS, speculative connections, cache/worker paths, out-of-process targets and existing connections remain open hardening work. See ADR 0012. Permission/download/external-launch hooks are implemented as described in the permission-model checkpoint; other capability paths remain incomplete.
