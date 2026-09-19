# F06 HTML file chooser correction — 2026-09-14

Scope: the demonstrated OOPIF HTML file chooser bypass only. Original severity:
**Medium policy defect**; no silent file theft was reproduced. Screen capture and
the other F06 capability categories are not changed by this correction.

Status: **Fixed and locally regression-tested.** Build passed with zero warnings
and errors. Full native-enabled suite: **262 Core passed, 87 App passed, 3 optional
tests skipped** (two live subscription downloads and the separately run historical
collector). The original reproduction was also run separately and completed
successfully. [Recorded evidence](security-evidence/f06-chooser-fixed-2026-09-14.json)
includes native results, original picker observations and candidate assembly hashes.

## Cause and enforcement point

`BrowserCapabilityGuard` previously called
`Page.setInterceptFileChooserDialog(enabled:true,cancel:true)` only on the root
CDP target. Its `Page.fileChooserOpened` receiver displayed Core's explanation;
that notification was not the interception boundary. Same-process frames used
the configured target, while an OOPIF had its own unconfigured target and could
open Chromium's native Windows chooser.

The installed WebView2 SDK **1.0.4129.50** does not expose a cancellable HTML
file-selection event on `CoreWebView2` or `CoreWebView2Frame`. Frame navigation
events do not intercept HTML input clicks. Microsoft provides the supported
[`CallDevToolsProtocolMethodForSessionAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.calldevtoolsprotocolmethodforsessionasync)
transport for separate targets, explicitly including cross-origin iframes, and
documents flattened sessions and auto-attachment. The chooser operation itself
is [CDP `Page.setInterceptFileChooserDialog`](https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-setInterceptFileChooserDialog),
not a dedicated stable WebView2 frame event.

[`FileChooserGuard`](../src/Zenith.App/Navigation/FileChooserGuard.cs) now:

1. Installs root cancellation before evaluating the Core file-selection decision.
2. Configures [CDP auto-attachment](https://chromedevtools.github.io/devtools-protocol/tot/Target/#method-setAutoAttach)
   for iframe targets with `waitForDebuggerOnStart=true` and flattened sessions.
3. Checks that a newly discovered iframe target is paused, installs its chooser
   decision, enables recursive attachment in that target, and only then resumes it.
   A second diagnostic session on a tracked target needs no separate grant.
4. Tracks detachment/recreation and stops setup for destroyed targets. Initialization
   failures, unavailable/invalid decisions, malformed required events and command
   failures/timeouts have no allow fallback. Runtime failure marks the tab unready
   and invokes the existing window/controller disposal path. Commands have a
   ten-second deadline; paused targets are not resumed on failure or disposal.

`BrowserCapabilityGuard` supplies `BrowserCapabilityPolicy.Evaluate(FileSelection)`;
the App adapter does not classify sites or invent permissions. Core remains
unchanged and **currently denies all file selection**. This task introduces no
upload grant, Vault change or general permission system. The installed decision
is fixed for the target lifetime under that immutable policy; a future mutable
or per-origin grant system must explicitly redesign decision propagation and
revocation rather than treating this adapter as a complete grant implementation.

An explicit allowed decision leaves Chromium's real native chooser enabled. The
adapter never provides paths, opens a replacement picker, calls
`DOM.setFileInputFiles`, or creates web filesystem handles. Allowed behavior is
tested with a synthetic injected decision, not a production policy exception.

## Validation

[`FileChooserScenario`](../tests/Zenith.App.Tests/Navigation/FileChooserScenario.cs)
runs on fresh profiles and loopback sites with normal Chromium site isolation.
Native WebView2 process/frame information verifies separate OOPIF and nested-OOPIF
renderer processes. The test observes visible Windows dialog windows belonging
only to its own browser processes; it also observes renderer results and an
independent TCP upload receiver.

Tested runtime: **152.0.4191.66**, SDK **1.0.4129.50**. Final denial-run renderer
PIDs were root 19296 / OOPIF 22052 / nested OOPIF 22304; allowed-run PIDs were
3900 / 19936 / 24620. No site-isolation, certificate or sandbox override was used.

| Context | Denied decision | Allowed adapter decision |
| --- | --- | --- |
| Top-level | No chooser, input cancellation, zero files, empty value, no upload | Real chooser selected synthetic fixture; renderer read expected contents; server received upload |
| Same-origin iframe | Same denial assertions | Same selection/upload assertions |
| Nested same-origin iframe | Same denial assertions | Same selection/upload assertions |
| Cross-origin, verified OOPIF | Same denial assertions | Same selection/upload assertions |
| Nested cross-origin, verified OOPIF | Same denial assertions | Same selection/upload assertions |

Allowed tests enter only the isolated synthetic file into the actual Windows
dialog and activate its Open action. This is test automation of the user-controlled
flow, not CDP file injection. The page receives the filename and Chromium's masked
input value, never the actual directory path. Its ordinary `File.text()` and fetch
upload provide positive controls for both renderer access and server observation.
Denied cases assert no selection or successful read and no request at that upload
endpoint, rather than merely accepting a browser-side error.

The suite also exercises cross-process subtree destruction/recreation and
same-origin/cross-origin process transitions. Injected unavailable, null and
invalid policy decisions during child setup invoke controller disposal before
the child resumes its inline script. This demonstrates the pause boundary using
absence of a child script's independent server request.

The original account-boundary reproduction was rerun: its root/OOPIF HTML picker
records show no native dialog and zero selected files, with input cancellation
for gesture-triggered requests. Requests without a gesture remain refused by
Chromium; that refusal is not credited solely to Zenith. Historical pre-fix F06
evidence is retained in [the original investigation](account-boundary-validation.md).

## Reproduction

```powershell
dotnet build Zenith.slnx --no-restore
$env:ZENITH_WEBVIEW_TESTS='1'
$env:ZENITH_FILE_CHOOSER_TESTS_ONLY='0'
$env:ZENITH_FRAME_TESTS_ONLY='0'
$env:ZENITH_STRICT_CONNECTION_TESTS='0'
dotnet test Zenith.slnx --no-build --logger 'trx;LogFilePrefix=f06-final' --results-directory build/f06-test-results
```

Use `ZENITH_FILE_CHOOSER_TESTS_ONLY=1` to run just the native chooser matrix in
the existing WPF master fact. Full-suite verification uses **0**. This shares the
existing single WPF application runner and preserves the other native scenarios.

## Limits

- The protocol's experimental chooser cancellation and target lifecycle behavior
  require regression testing on WebView2 updates. A supported WebView2 CDP transport
  does not make every underlying Chromium command a stable WebView2 API contract.
- There is no JavaScript patch and no TLS, origin-isolation or sandbox change.
  Native dialog visibility and a controlled selection/upload are experimentally
  validated, not a proof against every Chromium or Windows defect.
- Production attachment uploads remain intentionally unavailable until Core has
  an explicit grant design. The positive controls establish adapter compatibility,
  not approval for a new capability flow.
- The tests cover ordinary HTML file selection, not every OS dialog variant,
  accessibility workflow or multi-file/directory upload UI. File System Access,
  screen capture and other capability assessments keep their separate scope.
- This closes the reproduced HTML chooser policy gap locally; it is not independent
  security review or an overall primary-account readiness approval.
