# F02 fix validation — 2026-09-13

**Fixed and locally regression-tested; original severity High.** The reproduced
denied OOPIF document execution and 307/308 POST-body delivery are closed on the
tested candidate. This is not an overall primary-account release approval or an
independent review. F06 and the other findings retain their separate dispositions.
The [original investigation](account-boundary-validation.md) and its pre-fix JSON
remain historical evidence.

## Implementation

[`DocumentRequestGuard`](../src/Zenith.App/Navigation/DocumentRequestGuard.cs)
subscribes to native frame navigation, recursively attaches to newly created
frames, and removes frame tracking on destruction. Every redirect is evaluated
again. The handler defaults to cancellation and accepts only an explicit Core
Allowed decision; initialization, subscription and protection failures have no
unrestricted recovery. Root CDP Fetch remains additional request-stage protection.

[`DocumentRequestPolicy`](../src/Zenith.Core/Navigation/DocumentRequestPolicy.cs)
remains the authority: mandatory Blacklist precedence, current top-level
authorization and the existing Greylist embedding rule all apply. A narrow Core
case preserves inherited `about:blank`/`about:srcdoc` frame functionality previously
outside the network interceptor. It requires an authorized top-level document and
does not affect top-level navigation or Chromium origin assignment. The full-suite
run initially caught the missing inherited-document compatibility; it passed after
this correction and Core boundary tests were added. See [ADR 0017](decisions/0017-native-frame-document-enforcement.md).

## Strict regression evidence

[`FrameDocumentEnforcementScenario`](../tests/Zenith.App.Tests/Navigation/FrameDocumentEnforcementScenario.cs)
runs twice: production guards intact, then root Fetch disabled **only in the test**.
Both passed on WebView2 **152.0.4191.66**, SDK **1.0.4129.50**. Native process/frame
IDs verified same-process and separate-process frames, not merely cross-origin
URLs. Final full-run renderer PIDs were root 19260/OOPIF 5160 and root 8828/OOPIF
10560 respectively. Fresh isolated profiles and literal-loopback HTTP fixtures
were used; no certificate, sandbox or site-isolation override was used.

| Case | Assertion and result |
| --- | --- |
| Direct denied frame from root, ordinary same-process child, verified OOPIF | Native cancellation observed; denied fixture execution marker absent. Passed in both modes. |
| Nested frames from ordinary child and OOPIF | Same Core decisions and cancellation. Passed. |
| 301/302/303/307/308 POST redirects, denied and allowed sinks | All five redirect events re-evaluated in all three contexts, both modes: 60 redirect cases. Passed. |
| Denied 307/308 redirects | Allowed relay first received the synthetic password/token body; independent denied server received **no sink request**. No POST or synthetic body reached the denied server throughout either matrix. Passed. |
| Allowed redirect compatibility | Independent Greylist sink received GET without body for 301/302/303 and POST with intact synthetic body for 307/308. Passed. |
| Greylist embedded documents beneath Whitelisted top-level page | Loaded, executed positive marker, and reached server without a separate grant. Passed. |
| Mandatory Blacklist versus Whitelist membership | Core returned Blacklisted and native OOPIF-child navigation was cancelled. Passed; this HTTPS synthetic-host check is not a TLS-server transport test. |
| Policy becomes unavailable | New OOPIF-child document cancelled. Passed. |
| Frame destruction/recreation | Recreated allowed child loaded after descendants were removed. Passed. |
| Top-level navigation, redirects, popups, native account scenarios | Existing full native regression master passed, including authentication flows and F03 failure/crash clearing. |
| Inherited documents | Existing native srcdoc/blank-frame positive controls passed; new Core tests cover top-level refusal, invalid targets, missing/denied parent and expired temporary authorization. |

Independent TCP listeners record request paths, methods and bodies; positive
controls verify receiver availability, and allowed redirects verify that Chromium
can preserve the form body. A browser-side error alone does not satisfy the
307/308 regression. Execution checks use the same parent message marker as allowed
positive controls; they do not infer script blocking merely from missing requests.

**Request-timing limit:** native cancellation can occur after an initial denied
GET or 302/303 redirected GET reaches the server. The fix blocks the document
from executing, but must not be described as preventing every denied request or
possible URL/cookie disclosure. Server-side absence of denied POST delivery is
experimentally established for this matrix, not a Microsoft guarantee covering
every future runtime/protocol. Keep these regressions for WebView2 updates.
No TLS interception, JavaScript security boundary, or network-firewall change was
introduced.

The original evidence collector was also rerun separately on the final candidate
(1 harness passed). The [post-fix F02 observations](security-evidence/f02-fixed-2026-09-13.json)
retain its F02 records: OOPIF denied document `executed=false`, allowed embedding
`executed=true`, and empty denied-server observations for both 307 and 308. They
also explicitly record the denied GETs that arrived before cancellation. The
collector records observations rather than asserting every security property;
the strict matrix above supplies the regression assertions.

## Reproduction and verification

From the repository root on Windows with WebView2 installed:

```powershell
dotnet restore Zenith.slnx
dotnet build Zenith.slnx --no-restore
$env:ZENITH_WEBVIEW_TESTS='1'
$env:ZENITH_FRAME_TESTS_ONLY='0'
$env:ZENITH_STRICT_CONNECTION_TESTS='0'
dotnet test Zenith.slnx --no-build --logger 'trx;LogFilePrefix=f02-full' --results-directory build/f02-test-results
```

Build: **0 warnings, 0 errors**. Complete suite: **262 Core passed; 87 App passed,
3 optional tests skipped** (two live-list downloads and the separately run
historical evidence collector). Native tests were enabled. Setting
`ZENITH_FRAME_TESTS_ONLY=1` runs just the new native matrix within the master fact;
it was **0** for the complete suite. Existing connection-test opt-in configuration
is unchanged; it does not disable the new F02 assertions.

Candidate assembly SHA-256 values:

| Assembly | SHA-256 |
| --- | --- |
| Zenith.App.dll | `50CA1AB022628B45868613235EAC9FD00EF5B04C6F29DB7DE6534E79F99018BB` |
| Zenith.Core.dll | `0864A442E8D965F0720101733535929A70275512FB7FC08E373D5DEBAA95DCD0` |
| Zenith.App.Tests.dll | `C887624BDE81C826E571DC52948DB07724BEB45D354E99A4E341351F20000723` |

Microsoft's supported [frame NavigationStarting](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2frame.navigationstarting)
and [FrameCreated](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2frame.framecreated)
APIs are the native integration points. Chromium still owns TLS validation,
sandboxing, cookies and origin isolation; the adapter makes no replacement for
those boundaries.
