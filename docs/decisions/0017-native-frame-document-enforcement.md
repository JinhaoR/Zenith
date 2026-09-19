# ADR 0017: Native frame-document enforcement

Date: 2026-09-13

Status: Accepted; locally validated F02 correction

## Decision

Root CDP Fetch interception did not cover a verified out-of-process frame's child
documents. F02 reproduced denied document execution and 307/308 credential-body
delivery. `DocumentRequestGuard` now also uses supported WebView2 native APIs:
root `FrameNavigationStarting`, recursive `CoreWebView2Frame.FrameCreated` and
`CoreWebView2Frame.NavigationStarting`, with tracked destruction and disposal.
Redirects are evaluated each time, even when their navigation ID is unchanged.

The adapter cancels by default and accepts only Core's explicit Allowed result.
It supplies the native top-level source to the existing `DocumentRequestPolicy`.
Core retains mandatory Blacklist precedence and Whitelisted-page Greylist
embedding. Core also recognizes exact embedded `about:blank`/`about:srcdoc` targets
under current top-level authorization, preserving functionality previously
outside the network-only interceptor. No arbitrary scheme exception is added.
Chromium remains responsible for inherited/opaque origins.

Root CDP interception remains request-stage defense in depth using the same Core
authority. It is not the OOPIF enforcement boundary. Subscription or protection
failure invokes the existing controller-disposal failure path. TLS, sandboxing,
site isolation and capability policy are unchanged.

## Evidence and limits

See [F02 fix validation](../f02-fix-validation.md) for native process verification,
the redirect matrix and independent server observations. Native cancellation
blocked document execution and denied 307/308 POST delivery on the tested runtime,
including when the test disabled root Fetch. Some denied GETs reached the server
before cancellation; this design is document-access enforcement, not zero-contact
network enforcement. Rerun the native matrix on runtime/SDK updates.

Microsoft documents recursive [frame creation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2frame.framecreated)
and cancellable [frame navigation, including redirects](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2frame.navigationstarting).
These supported events avoid depending on root CDP target coverage. Their presence
alone is not evidence that a request body was withheld; the server assertions
provide that evidence for the tested cases.
