# Account-security reassessment and review handoff

Status, updated 2026-09-13: **Primary-account readiness not yet established.**
F03 has been fixed and locally regression-tested. The 2026-09-13 focused
[account-boundary investigation](account-boundary-validation.md) reproduced F02:
a denied OOPIF document executed and 307/308 redirects delivered synthetic
credential bodies to a denied destination. F06 is narrowed to a confirmed OOPIF
picker-policy gap, screen-capture hardening and otherwise validated tested paths. F01 is Informational and no longer an
account-security release blocker. No Critical issue, cross-origin credential
theft, privileged bridge exploit, TLS bypass or sandbox escape was established
by the original review. This is not a claim that such defects are impossible.

This reassessment preserves the original F01-F11 IDs and changes their priority
against the user's clarified model. Zenith governs visible navigation/document
access and must safely host sensitive authenticated services. It does not promise
to block every background connection made by an authorized website. The owning
scope is in [the threat model](threat-model.md) and [site policy](site-policy.md).
Existing transport, resource and capability controls are not removed by this
documentation change. A gateway, WFP layer, separate profile per origin or ban
on ordinary website scripting is not an account-security prerequisite.

## Revised original findings

Severity reflects the issue supported by current evidence, not the maximum
impact of an assumed future exploit. Missing tests are distinguished from proven
unsafe behavior. "Before use" conditions below are this review's recommendation,
not a Microsoft certification requirement.

| ID | Original -> revised severity | Primary-account relevance and disposition |
| --- | --- | --- |
| F01 WebSocket request-filter bypass | High -> **Informational** | Not a demonstrated credential, origin, host or navigation-policy breach. Remove its former release-blocking classification; retain the coverage limitation. |
| F02 Incomplete frame/target coverage | High provisional -> **High confirmed** | Denied OOPIF grandchild executed; 307/308 nested redirects delivered synthetic credential POST bodies. Requires implementation change before primary accounts. See focused validation. |
| F03 Failed clear can retain authenticated documents | High -> **High** | Historical concrete failure path; fixed and locally regression-tested on 2026-09-13. Independent retest remains pending. No credential theft was demonstrated. |
| F04 Persisted-worker startup interval | Medium -> **Low** | Ordinary same-origin background activity is not a flaw. Review legacy profile migration and removal guarantees; no cross-origin access or credential leak established. Not a standalone blocker with a fresh, known profile. |
| F05 Executable-adjacent browser profile | Medium -> **Medium** | Deployment-dependent disclosure risk from copying/syncing browser state. Establish a private, known production profile before primary use. Default location alone does not prove insecure ACLs or plaintext cookies. |
| F06 Incomplete capability coverage | Medium -> **Medium** | OOPIF HTML chooser bypasses explicit cancellation policy but retains native file selection. No silent file theft reproduced. Tested FSA/persisted handles/clipboard/media paths validated; screen cancellation is hardening on current evidence. |
| F07 Legacy password filling | Medium -> **Low** | New-saving disabled does not remove existing filling. Conditional on legacy saved passwords; not cross-origin theft or inherently unsafe password-manager behavior. Fresh profile or informed cleanup resolves the legacy concern. |
| F08 Filtering inside WPF/Core process | Medium -> **Medium** | Data-controlled native parser exposure and synchronous UI work are real architectural risks; no native exploit established. Resource budgets/servicing matter; a separate process is hardening, not a mandatory rewrite. |
| F09 External runtime/debugging overrides | Medium -> **Low** | No unsafe override was observed. Validate production launch configuration before use. An actually exposed debugging endpoint or weakened TLS/sandbox setting would require separate, higher-severity treatment. |
| F10 Release gates and dependency servicing | Medium -> **Medium** | Assurance/maintenance gap, not a current credential exploit. Run relevant native acceptance checks on the candidate and establish servicing; the F01 zero-contact assertion is not the required gate. |
| F11 URLs in bookmarks/UI/diagnostics | Low -> **Low** | Conditional disclosure of signed/reset links through copies, support captures or dumps. No production cookie/password logger found. Preserve deliberate address inspection and accessibility; reduce unnecessary exposure. |

### F01: filtering coverage, not account compromise

`ResourceRequestGuard.cs:62` maps a Websocket context, but the native callback does
not arrive; `DocumentRequestGuard.cs:43` intercepts documents. The previous
experiments prove socket contact, not that Core authorized a forbidden page or
that another origin's cookies became readable. Keep socket-driven navigation and
bridge tests. Do not require a gateway or complete WebRTC/DNS blocking. The
[connection ledger](connection-coverage.md) retains the historical evidence and
explains why the unchanged strict runner can still return failure.

### F02: confirmed OOPIF document and credential-redirect gap

**Requires implementation change; High confirmed, 2026-09-13.** The focused
[validation report](account-boundary-validation.md) records actual separate
renderer processes, a Core-denied nested document executing, and 307/308 redirects
sending synthetic password/token POST bodies to the denied endpoint. Root-target
CDP Fetch observed the direct-child denial but missed the OOPIF grandchild;
recursive native frame-navigation observers saw both. Production neither installs
that recursive policy handling nor initializes child-target Fetch enforcement.
The separate resource filter does not substitute for the full site policy.

Top-level denied navigation/redirects and denied popups, including an OOPIF popup,
passed. Chromium origin isolation and the tested native message boundary passed.
The normal 307/308 preservation of a form's body is not a same-origin escape;
the failure is bypassing Zenith's explicit document decision. Fix frame/document
coverage and retest before primary accounts. Preserve intentional Greylist
embedding and Chromium security; the report gives the concrete remediation and
case-by-case F06 dispositions. No production change was made in this investigation.

### F03: removal must complete or destroy the controller

**Fixed and locally regression-tested, 2026-09-13; original severity High.**
The original implementation erased the tab URI and marked it as a start surface
before asynchronous `about:blank` clearing completed, ignored unsuccessful
completion, and used best-effort suspension after an exception. That could retain
an authenticated document after Home, revocation or a native boundary appeared.

`src/Zenith.App/MainWindow.DocumentClearing.cs` now owns the WebView2 adapter;
`Navigation/DocumentClearance.cs` owns the dispatcher-confined removal lifecycle.
The tab retains its document identity while clearing. The controller stays hidden;
page navigation and popups cannot revive it. New user navigation waits behind both
scheduled and issued clears. Only a matching successful `NavigationCompleted`
with a blank engine source confirms ordinary removal.

Unsuccessful/cancelled completion, a navigation exception, a ten-second dispatcher
deadline, or `ProcessFailed` during clearing destroys the affected WebView/controller
before publishing an empty tab. Deferred navigation is discarded on destruction.
The failed tab remains unavailable; the user can open a new tab, whose normal
initialization attaches all existing guards. If destruction throws, Zenith exits
with `Environment.FailFast` rather than claiming removal or continuing unsafely.
The deadline requires a responsive host dispatcher; it covers missing engine
callbacks and renderer hangs, not arbitrary suspension of the WPF process.

Validation: solution build passed with zero warnings/errors; 253 Core and 86 App
unit tests passed. The optional native browser suite passed on WebView2
152.0.4191.66. `DocumentClearingScenario` used isolated synthetic authenticated
content and checked successful deferred navigation, injected Navigate failure,
actual navigation cancellation, injected missing completion with the real deadline,
and an actual renderer crash triggered by test-only CDP `Page.crash`. Failure
assertions check controller disposal, truthful tab metadata, discarded navigation,
and prevention of reactivation. Deterministic lifecycle tests additionally cover
failed disposal, stale deadlines/completions and duplicate failure notifications.
No provider account or credential was used. These are local tests, not independent
review or resolution of other findings. Earlier diagnostic native runs exposed
already-disposed controller access (fixed), then intermittent redirect/network
scenario timeouts; the final complete native run passed.

Normal inactive tabs may remain logged in; Home is not provider logout, and
successful document removal does not revoke server sessions.
[Microsoft process-failure guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-related-events)
and [best-effort suspension](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.trysuspendasync)
support distinguishing hiding/suspending from completed removal.

### F04: persisted workers

`MainWindow.xaml.cs:194/252` opens the profile before `MainWindow.Security.cs:11`
removes workers. The unmeasured bootstrap interval is not evidence of account
theft. Ordinary service-worker persistence is normal browser functionality.
Use a known profile/migration path, verify cleanup when explicitly requested and
retain truthful lifecycle claims. Do not require external egress enforcement to
stop all pre-cleanup activity. Any demonstrated authenticated document retaining
execution after promised removal belongs to F03; origin isolation failure would
be a new security finding. Existing worker restrictions remain unchanged.

### F05: browser state is sensitive, but persistence is normal

`MainWindow.xaml.cs:194` creates the default WPF UDF. Cookies, site storage and
cached authenticated content deserve a known local per-user location, suitable
permissions, separation from development/shipping artifacts, and verified cleanup.
No insecure ACL or plaintext cookie exposure was demonstrated. A custom
LocalApplicationData profile is recommended; an equivalently verified private
deployment can address the risk without changing storage APIs. Do not copy live
profiles into reports, source snapshots or distributable archives. Shared tabs
in one profile and persistent sign-in are normal, not cross-origin disclosure.
[Microsoft UDF guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder).

### F06: validated controls, picker gap and screen hardening

The 2026-09-13 [focused experiments](account-boundary-validation.md) closed the
tested top-level file-picker, File System Access, persisted handle/permission,
clipboard text/rich-read and synthetic camera/microphone acceptance gaps. File
handles persisted as objects, but old grants were reset and file reads/renewal
were denied. OOPIF origin and capability checks are identified explicitly in the
report; these are scoped runtime results, not universal security certification.

OOPIF HTML file inputs did open a native chooser despite the no-selection policy:
**Requires implementation change; Medium policy defect**, with no silent read or
upload demonstrated. Root-only CDP chooser interception does not cover that target.
ScreenCaptureStarting remained uncancelled by production; the test cancelled it
before UI display. This is **Hardening only** for account-security evidence, not
proof of silent capture or validation of the complete consent experience.

Maintain the tested denials and repair the actual picker-policy gap. Do not add
speculative capability systems merely because dedicated handlers are absent.
Intentional denial of attachments/media can be a provider compatibility limitation.

### F07: legacy passwords

`BrowserCapabilityGuard.cs:33` disables saving, not filling existing saved
passwords. Settings already acknowledges this, and profile cleanup exists in
`MainWindow.Security.cs:78`. A compromised matching origin can read data delivered
to it, but that is not proof of a Zenith-specific credential leak. Use a fresh
profile or obtain informed cleanup of legacy saved credentials when the intended
configuration excludes password filling; do not silently delete user data.
[Microsoft documents the distinction](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.ispasswordautosaveenabled).

### F08: native parser exposure and UI availability

`AdblockEngine.cs:23` loads fixed bundled JavaScript in ClearScript/V8, exposes no
CLR objects and bounds data/execution. It does not execute arbitrary website
JavaScript in the host. `CosmeticFilterGuard.cs:66` still performs synchronous
filtering with per-controller budgets, which can delay UI/revocation under load.
Keep the native dependency updated and bound application-wide work; move expensive
work off the dispatcher where feasible. A restricted helper process is worthwhile
defense in depth but not required absent a demonstrated exploit or unmanageable
availability problem. If overload defeats reliable removal, resolve that as part
of F03 acceptance. Do not imply that an unspecified V8 vulnerability is confirmed.

### F09: production configuration

`AreDevToolsEnabled=false` in `BrowserCapabilityGuard.cs:35` restricts the UI, not
all externally configured debugging. Verify the actual runtime/channel, profile,
environment/registry overrides and absence of unintended debugging endpoints or
security-disabling flags before primary use. No hostile configuration was found;
arbitrary same-user malware remains outside the claimed tamper boundary. A clean
supported launch does not require a new anti-malware or registry-policing system.
Microsoft documents overrides and standard-user hosting in its secure-hosting
guidance linked above; elevation is not a mitigation.

### F10: candidate verification and servicing

`.github/workflows/ci.yml` does not enable native WebView2 tests. The optional
security runner also retains the superseded F01 assertion. Require account-focused
native checks for the actual candidate, and update future release automation to
match them. Keep WebView2, Zenith, ClearScript/native V8 and bundled dependencies
serviced; Evergreen updates do not update all application dependencies. Formal CI
automation and signed distribution are release hardening; a manually verified
local candidate is not automatically unsafe for lacking them. Do not require a
passing zero-contact test to accept ordinary website functionality.
[Microsoft recommends current serviced runtimes](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/enterprise).

### F11: secondary disclosure paths

`BookmarkStore.cs:54` writes complete URLs to JSON; `MainWindow.xaml.cs:1297/1440`
uses addresses in native UI/accessibility metadata. These can contain bearer links.
That matters when files or screenshots are shared, but ordinary bookmarks and
deliberate address inspection are also normal browser features. No production
logger was found recording cookies, authorization headers, request bodies or
plaintext passwords. Minimize incidental token-bearing UI/log data; preserve
accessible origin/address inspection; document that bookmark retention differs
from browsing-data cleanup. Do not claim that managed strings can be reliably
erased or automatically upload raw profiles/crash dumps.

## Before using a primary account

1. F03 implementation and local removal/failure regression checks are complete.
   Preserve those checks for release candidates; independent retest remains pending.
2. Fix and strictly retest the reproduced F02 frame-document/credential-redirect
   defect. Repair F06's OOPIF HTML picker-policy gap. Retain the now-passing
   origin, native bridge, persisted-file and clipboard/media cases; screen-capture
   consent remains a specifically documented hardening/validation limit.
3. Establish a private, known production profile and clean launch configuration
   (F05/F09); handle legacy passwords/permissions when reusing a profile. Verify
   intended cleanup without equating cookie persistence with a vulnerability.
4. Run account-focused native checks on the identified candidate and check current
   runtime/dependency servicing (F10). For the actual intended provider, test the
   login, MFA, redirects, cancellation, logout/restart and cleanup flow with a
   disposable account first. This is a compatibility and integration prerequisite
   for this recommendation, not proof of a vulnerability or an obligation to test
   every provider/MFA method before using any account.

The concrete code fix established here is F03. Other prerequisites include
targeted validation and deployment choices; code fixes depend on what those checks
show. Independent review of these boundaries is valuable for this new browser,
but neither its absence nor failure to support a provider is itself a vulnerability.

## Later work and residual risk

Improve filtering isolation/throughput (F08), profile migration/management,
diagnostic hygiene (F11), explicit capability UX, production override diagnostics,
and automated native release gates. F04/F07/F09 are conditional low risks, not
unconditional account blockers. F01's former High rating and gateway remediation
are withdrawn. Background communication, persisted same-origin workers, ordinary
password-manager behavior and intentional print/save actions are not findings of
credential theft. Existing controls are not being removed here.

After the prerequisites, Zenith would still differ from normal Microsoft Edge:
its custom WPF/CDP/bridge/filtering code adds attack surface; its lifecycle,
profile/permission management and release process have less validation; provider
SSO/device/passkey and popup compatibility can differ; and native V8/application
dependencies have separate servicing. Chromium supplies important shared security
mechanisms, but sharing the engine is not proof of complete browser equivalence.
No specific missing Edge cookie-encryption guarantee was established in this
review. Phishing, a compromised permitted origin, browser zero-days and
same-user/administrator malware remain ordinary
risks; the Vault is not a lock or encryption layer for email sessions. Zenith's
access restrictions must not be presented as making an allowed site trustworthy.

## Historical verification and handoff

The original 2026-09-12 review recorded a clean Release build, 253 Core tests and
78 App tests passing with native WebView2 enabled, two optional subscription tests
skipped, and the F01 strict assertion failing. Runtime was 152.0.4191.66; NuGet and
npm advisory checks reported no known vulnerabilities then. No tests were rerun
and no implementation changed during this documentation-only reassessment. These
results are dated evidence, not proof that the unresolved lifecycle/capability
cases passed. The earlier 2026-09-09 checkpoint below remains historical.

Real-provider work uses the step-by-step [MFA acceptance test](provider-mfa-testing.md).
No disposable account has been supplied and no live provider result is claimed.
No independent reviewer has been assigned. This handoff and implementation-agent
tests do not constitute an independent security review.

Implementation checkpoint results: Debug and Release builds succeeded with no
warnings/errors; 253 Core and 62 App tests passed with native WebView2 tests enabled.
Two optional live-subscription tests were skipped. The NuGet advisory check on
2026-09-09 reported no known vulnerable packages from the configured sources.
This excludes application flaws, runtime configuration and bundled JavaScript
advisories, and is not a continuing guarantee.

## Evidence to reproduce

Use disposable browser profiles and synthetic credentials. Never put a primary
account password, OTP, recovery code, session cookie or authorization header in
test logs, screenshots or source control. Capture the tested commit, Windows
version, WebView2 runtime version and pinned package versions with the results.

```powershell
dotnet restore Zenith.slnx
dotnet build Zenith.slnx --configuration Release
$env:ZENITH_WEBVIEW_TESTS='1'
dotnet test Zenith.slnx --configuration Release --no-build --no-restore
dotnet list Zenith.slnx package --vulnerable --include-transitive
```

The browser test includes server-observed redirect prevention, controlled
password/OTP submission, legacy-worker cleanup, new-worker denial, shared-worker
and dedicated-worker probes, native certificate rejection, cookie isolation and
profile cleanup. Its sign-in fixture is not Google/Microsoft testing, a real OTP
verifier, OAuth protocol conformance, passkey assurance or a penetration test.
Live filter-subscription tests are separately opt-in.

## Real-provider matrix — all pending

The user or a designated tester must supply **disposable test accounts**, enter
their own credentials directly and use a separate test Windows/browser profile.
Do not send credentials to the coding agent. Do not silently expand the Whitelist
to make a test pass: record required exact hostnames and obtain deliberate policy
changes. A blocked provider is an acceptable outcome to document, not a reason
to weaken protections.

| Provider/path | Required checks | Current evidence |
| --- | --- | --- |
| Google account / Gmail | Direct sign-in, redirected sign-in, OTP/push, passkey/security-key prompt, cancel/retry, logout/restart | Not run |
| Microsoft personal / Outlook | Same checks; account switching and cross-tab session behavior | Not run |
| Organization / Entra | Tenant consent, conditional access, device requirements and MFA recovery/cancel | Not run |
| Other intended mail provider | Exact login/callback hosts, OTP flow, sign-out and data cleanup | Not run |

For each, verify the native origin caption at every credential step, no secrets
in the caption/diagnostics, no unexpected external application or file/device
access, session isolation, revoked/expired destination handling and no insecure
redirect continuation. Test HTTPS with valid public certificates without adding
test roots or bypass flags to the application.

Provider compatibility is not guaranteed. Google's OAuth policy restricts
embedded user agents; a refusal must not be bypassed through user-agent spoofing.
[Google OAuth policies](https://developers.google.com/identity/protocols/oauth2/policies).
Microsoft documents runtime/authority limitations for WebView2 with MSAL; that
documentation is not proof of Zenith website-sign-in compatibility.
[Microsoft WebView2 authentication guidance](https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/webview2).

## Independent reviewer scope — not yet assigned

Provide the reviewer the diff, ADRs 0012/0014/0015/0016, threat model, permission
model, site policy, architecture and reproducible tests. Request a written report
with severity, reproduction, affected runtime, proposed remedy and retest evidence.

- Trace every navigation/resource entry point into Core, including POST redirects,
  frames, OOPIFs, popups, inherited documents, cache restoration and worker sources.
- Independently observe startup/restart, tab closure, cleanup failure and policy
  revocation, especially whether authenticated documents remain alive after
  promised removal. Treat worker/transport/speculative traffic as a security
  finding only when it violates an in-scope credential, origin, host, capability
  or lifecycle guarantee; ordinary background contact alone is insufficient.
- Exercise malformed/IDN/loopback addresses, TLS downgrade and certificate-error
  paths, native origin spoofing and native-response assignment failures.
- Review file-input/frame coverage, persisted File System Access handles, device
  prompts, credential storage, renderer permissions and crash recovery.
- Review host bridges, favicon decoding, filter-list supply chain and bundled JS,
  runtime/dependency updates, packaging and integrity of shipped binaries.
- Review Vault/authentication persistence, atomicity, clock rollback and recovery
  within the documented same-user/administrator threat boundary.

Primary-account readiness should be assessed against the prerequisites above and
the provider/account flows actually intended for use. Resolve material findings
and targeted acceptance gaps; exhaustive zero-contact coverage and every unrelated
provider flow are not prerequisites. Passing the current suite alone does not
demonstrate that the untested lifecycle/capability cases are safe.

## Reviewer report and acceptance

The reviewer must be someone other than the implementation author, with explicit
scope and authorized access to a sanitized candidate snapshot. Do not distribute
this workspace, private history or browser profiles automatically. The worktree
currently includes uncommitted changes; HEAD alone does not identify the tested
code. Include candidate assembly hash, sanitized source snapshot and pinned
dependencies. The local evidence runner does not upload anything.

For each finding record: ID, provisional/final severity, affected boundary,
preconditions, secret-free reproduction, expected/observed behavior, candidate
hash/runtime, remediation owner, and independent retest result. Distinguish
confirmed flaws from untested risks and compatibility refusals.

- Reviewer identity/organization and independence disclosure: **unassigned**
- Candidate snapshot/hash and runtime accepted for review: **pending**
- SEC-CONN-001/F01: **Informational; no longer an account-security blocker; filtering limitation retained**
- F03 lifecycle defect: **fixed and locally regression-tested; independent retest pending**
- F02: **confirmed High OOPIF document/credential-redirect defect; implementation required**
- F06: **OOPIF picker-policy fix required; other tested paths validated, screen hardening remains**
- Provider MFA results reviewed: **not run**
- Vault/persistence/permission/filter-supply-chain findings: **review pending**
- Critical/high findings resolved and independently retested: **not established**
- Residual risks and unsupported providers documented: **pending**
- Written primary-account release recommendation: **not approved**
