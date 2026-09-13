# F02/F06 account-boundary investigation — 2026-09-13

**Do not use primary accounts yet. F02 requires an implementation change.**
A Core-denied document executed beneath an out-of-process iframe. In the same
context, 307/308 redirects delivered synthetic password/token POST bodies to the
denied destination. No Chromium same-origin, native privilege, TLS or sandbox
escape was demonstrated.

Production sources were not modified. This investigation adds a test and evidence,
not an implementation fix.

## Candidate and reproduction

- Windows, .NET 10.0.6; WebView2 **152.0.4191.66**, SDK **1.0.4129.50**.
- Debug App DLL SHA-256:
  `eed1f2b7d3e2eedeea821e2f15ff7bba84a64ca5d6f10ee4fd8b605e1c386d62`.
- Production source inventory hashes matched before/after, including untracked
  sources; existing working-tree changes were preserved.
- [Investigation source](../tests/Zenith.App.Tests/Navigation/AccountBoundaryInvestigationTests.cs).
- [Recorded observations](security-evidence/f02-f06-2026-09-13.json).

Run separately from the existing WPF Application test:

```powershell
dotnet build tests/Zenith.App.Tests/Zenith.App.Tests.csproj --no-restore
$env:ZENITH_ACCOUNT_INVESTIGATION = '1'
$env:ZENITH_FAKE_MEDIA = '1'
$env:ZENITH_ACCOUNT_REPORT = Join-Path $PWD 'build/account-boundary-evidence.json'
dotnet test tests/Zenith.App.Tests/Zenith.App.Tests.csproj --no-build --filter FullyQualifiedName~InvestigateF02AndSensitiveF06
```

**A passing investigation harness means collection completed, not that every
security check passed.** It records the reproduced defects. Ordinary test runs
skip it unless explicitly enabled.

All accounts, passwords, tokens, files and clipboard data were synthetic. Profiles
were isolated; the legacy-handle seed browser exited before Zenith reopened its
profile. File dialogs were cancelled without selecting files. Screen requests
were stopped by a test observer before chooser display, avoiding desktop capture.

Literal-loopback HTTP uses Zenith's existing development exception. TLS,
certificate validation, sandboxing and origin security were not weakened.
`--no-proxy-server` and a synthetic hostname mapping routed fixtures locally.
The mandatory-list hostname's HTTP transport is also forbidden, so its negative
result does not independently prove HTTPS mandatory-list enforcement. The
reproduced site-policy defect uses a transport-eligible literal-loopback target.

Initial runs returned `NotFoundError` for media requests because no device was
available. That was inconclusive, not a security pass. The final run added
`--use-fake-device-for-media-stream`, **without automatic permission approval**,
to exercise real permission decisions using synthetic devices. Site isolation
was not forced or disabled. Multiple runs reproduced the document/picker gaps.

## Final classifications

“Closed by validation” applies to the tested candidate and paths, not every
possible Chromium defect. Relevant updates require retesting.

| Finding / part | Classification | Evidence and account relevance |
| --- | --- | --- |
| **F02 overall** | **Requires implementation change** | **High:** denied nested document execution and credential-bearing redirects reproduced. |
| Top-level address/script navigation | Closed by validation | Blacklisted and ungranted Greylisted destinations denied; original document cleared; no corresponding denied endpoint request. |
| Top-level 302/303/307/308 form redirects | Closed by validation | Allowed form endpoint received submission; denied sink received no redirected request/body. |
| Denied popups/new windows | Closed by validation | No new tab or destination request, including a popup from a verified OOPIF. |
| Nested/OOPIF documents and authentication redirects | Requires implementation change | Denied grandchild executed. 302/303 reached the sink by GET; 307/308 delivered the form body. |
| Cross-origin cookies, DOM, local/session storage, IndexedDB and authenticated response reads | Closed by validation | Cross-origin access refused; HttpOnly secret absent from script-visible cookies; credentialed fetch response unreadable without CORS. |
| Web messages/native services | Closed by validation | Live cosmetic bridge, Vault present, positive CSS replies verified; forged commands caused no state/file changes; host-object calls denied. |
| F06 top-level HTML picker | Closed by validation | Gesture-triggered selection cancelled; zero files. Existing separate regression covers same-origin nested HTML inputs. |
| **F06 OOPIF HTML picker** | **Requires implementation change** | **Medium policy defect:** native chooser opened despite explicit no-selection policy. No silent read/upload demonstrated; cancellation returned zero files. |
| F06 File System Access open/directory/save pickers | Closed by validation | Top-level gesture calls aborted by Zenith interception; cross-origin OOPIF calls refused by Chromium with/without gesture. |
| F06 persisted handles/permissions | Closed by validation | Handle object persisted, but old Allow setting was reset; read and renewal denied; cross-origin handle transfer produced `messageerror`. |
| F06 clipboard text and rich reads | Closed by validation | Focused root/OOPIF, with/without gesture: native denial and `NotAllowedError`. |
| F06 camera/microphone | Closed by validation | With synthetic devices available, root/OOPIF requests reached native denial; no stream. |
| **F06 screen capture** | **Hardening only** for account-security evidence | Production left `Cancel=false` before native consent. No silent capture reproduced; this does not validate blanket Zenith denial. |
| Attachments/device-dependent login/conferencing | Compatibility limitation | Intentional file/media denial may prevent features; no real-provider certification was performed. |

## F02: reproduction and implementation cause

A Whitelisted origin A embeds Whitelisted origin B. C is Blacklisted in the
actual `SitePolicySnapshot` consumed by Core. An otherwise-Greylisted origin is
the positive embedding control. Core correctly denies C and permits the Greylisted
frame under Whitelisted A, matching [Site Policy](site-policy.md).

`GetProcessExtendedInfosAsync` identified different renderer PIDs for A and B and
the expected parent FrameId; `Target.getTargets` also identified B as an iframe
target. This is verified OOPIF coverage. Microsoft describes the native
[process/frame association API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.getprocessextendedinfosasync).

Reproduction:

1. A embeds B.
2. B creates an iframe to C. An equivalent iframe directly beneath A is blocked.
3. Beneath B, C receives the GET and its script posts its loaded marker. Both
   server receipt and document execution were observed.
4. B submits a password/token form into another nested frame; B's endpoint
   redirects to C.
5. 307/308 preserve the method/body. C receives
   `password=synthetic-oopif-password&token=synthetic-oopif-token`.
   Equivalent top-level redirects were blocked.

POST preservation is normal Chromium behavior. Zenith's defect is missing the
Core document decision in this context. The test does not show an arbitrary page
extracting an email origin's password: the synthetic form owns its submitted
values. The realistic failure is an embedded authentication/application flow, or
compromised embedded content, continuing through a destination Zenith explicitly
rejects. It also permits excluded content to display and execute.

Relevant implementation and passive observations:

- [DocumentRequestGuard.cs](../src/Zenith.App/Navigation/DocumentRequestGuard.cs),
  lines 26–43, subscribes/enables Fetch on the root target; line 84 correctly
  delegates requests it sees to Core. Production does not install matching
  interception in child target sessions.
- Root `Fetch.requestPaused` observed the denied direct child, **not the
  successful OOPIF grandchild**.
- [ResourceRequestGuard.cs](../src/Zenith.App/Filtering/ResourceRequestGuard.cs),
  line 65, delegates to
  [ResourceFilteringPolicy](../src/Zenith.Core/Filtering/ResourceFilteringPolicy.cs).
  That policy uses the mandatory external list and advertisement engine, not
  the full site-policy snapshot; it cannot replace C's missing document decision.
- Production has no recursive native frame-navigation policy handler. Test-only
  `CoreWebView2Frame.NavigationStarting` observers saw the grandchild and credential
  sink. The native event exists; Zenith is not using it to decide these navigations.

Microsoft recommends checking page/frame navigation and restricting native
interfaces in its [secure-hosting guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).
The supported [frame navigation event](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2frame.navigationstarting)
can cancel navigation. The supported
[session-specific CDP API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.calldevtoolsprotocolmethodforsessionasync)
does not automatically install root-target interception in child sessions.

Concrete remediation, to implement and then validate:

- Route applicable frame navigation recursively through `DocumentRequestPolicy`,
  retaining real top-level authorization and the intended Greylist embedding rule.
  Include redirects, dynamic frames, target/process changes and destruction.
- Close the **document-request** interception gap across child targets/sessions
  before releasing their document requests. Do not assume navigation cancellation
  alone prevents an already-sent credential-bearing POST.
- Centralize target lifecycle handling in the App adapter; Core remains the policy
  authority. Unknown/uninitialized document contexts get no unrestricted fallback.
- Turn these cases into strict regressions: no denied document execution and no
  credential-bearing request at the sink, including newly created, already-running
  and process-swapped OOPIFs.
- Preserve TLS, sandboxing, origin isolation and allowed embedding. Do not disable
  site isolation or use JavaScript replacements to hide the defect.

This is a remediation direction, not a claim that an unimplemented design is
race-free.

## F06 and trust-boundary interpretation

[BrowserCapabilityGuard.cs](../src/Zenith.App/Navigation/BrowserCapabilityGuard.cs),
line 40, installs root-target `Page.setInterceptFileChooserDialog` cancellation.
Top-level File System Access calls explicitly reported interception. No speculative
extra mechanism is needed for that tested path. OOPIF HTML input opened a native
chooser because its target lacked equivalent interception. Fix target coverage
to honor the current no-selection policy. A user-approved file selection is normal
browser consent, but falls outside Zenith's current explicit capability model.
The test selected no file and did not demonstrate approval-free file exposure.

The persisted-handle positive control used a **test-only native file grant**,
IndexedDB persistence and a verified persisted `FileReadWrite=Allow` setting.
After complete browser-process exit, Zenith removed Allow; the handle queried
`prompt`, `getFile()` failed and `requestPermission()` was denied. Microsoft warns
that [native-created filesystem handles grant unusual access](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environment.createwebfilesystemfilehandle).
Production exposes no such grant API. This validates the seeded handle/grant
scenario, not every historical profile or a concurrently running foreign controller.

Clipboard/media results demonstrate Zenith-specific denial on top of Chromium's
origin/focus/gesture rules. Microsoft documents
[frame permission propagation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2permissionrequestedeventargs);
the Core handler can receive frame requests. A missing separate frame permission
handler is not itself a vulnerability. The delegated-frame permission URI observed
was the top-level origin: harmless under universal denial, but not sufficient
identity information for blindly authorizing future per-origin grants.

For screen capture, the test observed production `Cancel=false`, then **the test**
cancelled. The resulting `NotAllowedError` must not be credited to Zenith.
Microsoft's [ScreenCaptureStarting documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2screencapturestartingeventargs)
places this event before native chooser display. Explicit cancellation would make
blanket-denial policy consistent. This run did not select a screen, inspect chooser
origin labels or test every consent gesture; the complete screen-sharing consent
experience is not certified. Missing cancellation is hardening/policy consistency
on this evidence, not demonstrated silent screen theft or an independent blocker.

The cosmetic bridge positive control returned only `kind/token/url/css` in root
and OOPIF. Vault/file/execute command names and privileged-looking fields attached
to valid queries changed neither Vault bytes nor the synthetic file. Direct
host-object invocation returned access denied. Source inspection of
[CosmeticFilterGuard](../src/Zenith.App/Filtering/CosmeticFilterGuard.cs) and
[CosmeticMessage](../src/Zenith.App/Filtering/CosmeticMessage.cs) confirms bounded
CSS query/reply handling, not a native command dispatcher. These tests plus that
narrow implementation support closing this acceptance gap; finite probing alone
cannot prove the absence of every exploit.

## Primary-account decision

**F03's fix is insufficient by itself. Fix and strictly retest F02 before primary
email, GitHub or university logins.** Repair the OOPIF picker-policy gap in the
relevant target-coverage work. Validated F06 paths do not justify new production
mechanisms merely because a separate API handler is absent.

Normal Edge also preserves 307/308 bodies and permits user-approved file selection;
it does not promise Zenith's site-classification boundary. This finding is failure
of Zenith's additional document policy, not loss of Chromium origin isolation.
Real-provider compatibility, servicing and prior deployment/profile prerequisites
remain outside this focused investigation.
