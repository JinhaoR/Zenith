# ADR 0015: Reduce Native Account-Browsing Attack Surface

- **Status:** Accepted development checkpoint
- **Date:** 2026-09-09

## Decision

The user requested further protection for primary-email use. Prioritize concrete native-host exposure and fail-closed behavior without granting new login domains or claiming production readiness.

- Cancel HTML file-input dialogs through `Page.setInterceptFileChooserDialog` with cancellation enabled, before tabs become ready. Disable external WPF WebView2 drops on initial and new controllers. Core supplies the denied-capability explanation; no selected file is passed to a page.
- Convert list/engine exceptions into denied resource decisions. Catch native metadata failures and assign a local denial; if the native response cannot be assigned, close browsing controllers. Do not let exception paths implicitly allow requests.
- Remove `BitmapImage(websiteProvidedUri)`. That path could cause native URL or filesystem resolution outside browser filtering. Obtain PNG image streams from WebView2 instead; enforce byte and dimension limits before decoding, freeze decoded images and discard stale asynchronous results. Missing icons use a native globe. Existing themed brand geometry remains.
- Show the normalized engine origin in the existing Windows caption. A website title cannot replace that identity, and URL query/fragment tokens do not appear there. Keep full-address inspection on Ctrl+L and the existing identity command.

## Verification

Tests cover denied capabilities and filter exceptions in Core; real top-level/nested file-input cancellation with user gestures; local 403 and server-side absence of requests when an engine throws; native caption identity despite a forged page title; bounded PNG decoding; and Secure/HttpOnly fixture-cookie isolation. Existing TLS, redirect, frame, grant, cleanup and Vault regressions remain enabled. Only isolated profiles and synthetic account data are used.

NuGet's vulnerability query, including transitive .NET packages, reported no vulnerable packages from the configured advisory sources on 2026-09-09. It does not audit bundled JavaScript, the installed WebView2 runtime, application logic or future advisories.

## Remaining decisions and limits

This is not an independent audit or a guarantee that primary email is safe. File System Access pickers/old handles, out-of-process frame enforcement, full worker/connection lifetime control, and provider-specific login/MFA behavior remain open. Blocking HTML file selection means ordinary attachment uploads are unavailable for now. Existing explicit-HTTP policy remains unchanged pending the user's HTTPS-only choice. No password, cookie or Vault data is silently deleted.

## References

- [Chrome DevTools: native file-chooser interception](https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-setInterceptFileChooserDialog)
- [Microsoft: external drop control](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controller.allowexternaldrop)
- WebView2 API details were also checked against the pinned SDK's XML documentation.
