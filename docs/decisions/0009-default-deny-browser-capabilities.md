# ADR 0009: Default-deny browser capability boundary

- Status: Accepted first hardening checkpoint
- Date: 2026-09-07

Site access does not grant website capabilities. Until explicit capability grants exist, Core denies permission requests, downloads and external-application launches. Unknown capability values are denied too. This enforces the existing permission model without introducing new editable policy values.

App attaches a disposable WebView2 adapter to both the first controller and subsequent tabs before marking them ready. Event cancellation/denial is set before presentation. Permission results are not persisted in the renderer profile; download UI is suppressed, and active-tab denials use native notices. Detaching a tab removes the adapter's event subscriptions.

The implementation uses WebView2's documented [permission handling](https://github.com/MicrosoftEdge/WebView2Feedback/blob/main/specs/PermissionManagement.md), [download cancellation](https://github.com/MicrosoftEdge/WebView2Feedback/blob/main/specs/CustomDownload.md), and [external-scheme cancellation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.launchingexternalurischeme) hooks. External-scheme interception is defense in depth alongside unsupported-target navigation denial.

Core tests cover each capability and unknown values. Isolated renderer tests exercise real geolocation requests and blob downloads in initial and newly created controllers, checking denial, profile non-persistence and download cancellation. These tests do not establish coverage of every browser capability or older stored renderer grants. Frames, popup/redirect bypass matrices, safe recovery and stronger rollback resistance remain separate Phase 5 work. No automatic restoration of a previous protected envelope is introduced.
