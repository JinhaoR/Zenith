# Blank content after a failed background navigation

Investigated 2026-09-20 on WebView2 153.0.4234.32 with isolated, unsigned-in profiles.

## Confirmed failure

With `mail.google.com` allowed and `accounts.google.com` unlisted, a background
tab followed Gmail's redirects to Google sign-in. Core correctly denied the
sign-in destination. Native evidence before the fix:

```text
NavigationStarting: mail.google.com — allowed
NavigationStarting: accounts.google.com/ServiceLogin — cancelled
NavigationCompleted: IsSuccess=false, OperationCanceled, native Source=about:blank
WPF Source: https://mail.google.com/
After tab activation: WebView visible; both native surfaces collapsed
DOM: complete, empty body; renderer alive and resumed
Retry original URL: no new NavigationStarting event; native Source still about:blank
```

The cancelled document was never loaded. No renderer crash occurred. The dark
area was the empty controller's background, not hidden Gmail content. The same
failure was reproduced without Gmail using independent loopback redirect servers.

`MainWindow.Browser_OnNavigationStarting` cancelled the denied redirect but
presented a boundary only for the active tab. `Browser_OnNavigationCompleted`
likewise handled failed loads only for a visible active tab. The background tab
retained its earlier allowed `CurrentUri` and non-start state. `ActivateTab`
reauthorized that earlier address and exposed the empty controller.

`StartNavigation` then assigned the same WPF `Source` value on retry. Microsoft's
[`WebView2.Source` documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.wpf.webview2.source?view=webview2-dotnet-1.0.4129.50)
specifies navigation when setting a different value. The native and WPF sources
had diverged after cancellation; assigning the same requested URL did nothing.

## Focused correction

`MainWindow` retains per-tab failure presentation state, including the failed
destination. Denied or failed background loads enter the existing F03 clearing
lifecycle. Activation presents the failure and rechecks Core policy; stored
failure metadata never supplies authorization. A new explicit navigation or
return to the Sphere clears this presentation state. Explicit navigation uses
`CoreWebView2.Navigate`, including retries of the same address; native navigation
and document policy handlers remain in place.

No Core, frame enforcement, clearing-controller, capability, extension-loading,
TLS, sandbox or origin-isolation rules were changed. `accounts.google.com`
(plural) is a separate hostname from `account.google.com`. Neither is implicitly
authorized by allowing Gmail.

## Validation and limits

Before the fix, the native regression failed because activation left the boundary
collapsed. Afterward, `TabRenderingScenario` verifies background denied login
redirects, Blacklist redirects with no destination-server request, cancelled
allowed loads, retry of the exact URL, allowed login rendering, ordinary allowed
background tabs and retained-tab activation. It checks native completion, Core
policy outcomes, DOM content, WPF visibility/dimensions and painted pixels from
`CapturePreviewAsync`, rather than treating a populated tab label as rendering.

Live foreground Gmail sign-in (with its identity-provider hostname explicitly
allowed), Wikipedia and GitHub rendered successfully. Gmail sign-in also rendered
with Zenith's existing resource/cosmetic filtering enabled. Production uBO Lite
integration remains read-only status enumeration; it does not install or enable
an extension. The reproduction required neither uBO Lite nor cosmetic filtering.
No primary account credentials or existing browser profile were used. These
checks establish the reproduced lifecycle defect and sign-in-page rendering,
not compatibility certification for an authenticated Gmail mailbox.
