# ADR 0014: Account Security Development Checkpoint

- **Status:** Accepted development checkpoint
- **Date:** 2026-09-08

## Context and decision

The user requested security measures before using personal accounts. Browser-engine protections do not make Zenith's integration production-ready. Implement explicit adapter restrictions, native identity inspection and confirmed renderer-data cleanup without relaxing Core navigation policy or resetting the Vault.

`BrowserCapabilityGuard` rejects certificate exceptions, client-certificate selection and browser-level HTTP authentication. It disables host objects, password autosave, general autofill, developer tools, default context menus and default script dialogs before a tab is ready. Web messages remain enabled only for the existing bounded, frame-scoped cosmetic bridge, which has no authentication or policy services. Previously stored renderer permission grants are reset before browsing. Unsupported required APIs fail initialization rather than continuing with weaker defaults.

`DocumentRequestGuard` disables HTTP cache use and bypasses service-worker responses for the tab through CDP before enabling its document gate. This is a bounded mitigation, not a claim that workers cannot register or run, that existing connections terminate immediately, or that all out-of-process targets are covered.

Native website identity uses the engine's current URL, normalized ASCII hostname and explicit non-default port. It excludes path/query/fragment and distinguishes HTTPS from unencrypted HTTP without claiming the site itself is trustworthy. Explicit HTTP remains supported under existing Site Policy; HTTPS-only navigation requires a separate compatibility decision.

Settings offers renderer-profile cleanup only after a native confirmation that explains sign-out, lost unsaved page work and app closure. The shell prevents new navigation, disposes live controllers, clears `AllProfile` through a blank maintenance controller, then closes. A failure does not resume browsing or report successful sign-out. Vault storage, pending waits and Zenith bookmarks are not targets. No live user data is removed by development tests. Clearing data is not server-side account-session revocation or secure disk erasure. The supported profile API owns site-data removal; WebView2 does not support the tested CDP `ServiceWorker.stopAllWorkers` command, so Zenith does not use it or claim independent worker termination through it.

WebView2 Evergreen updates remain Microsoft-managed; a new-version event prompts a restart, and Settings displays the running engine version. This is not an updater for Zenith itself or proof that Windows has enabled runtime updates. SmartScreen is requested but remains subject to system settings.

## Review and limits

The production request/filter adapters do not log request bodies, authorization headers or raw exceptions. The cosmetic bridge uses bounded DOM hints, not credential fields. Stored browser credentials/cookies are WebView2 profile data, not the Vault's DPAPI credential verifier. Disabling autosave does not disable filling passwords saved previously; the UI directs users to confirmed data cleanup to remove them rather than silently deleting their credentials.

Tests cover canonical identity, default-deny capability decisions, native security settings, old permission grants, untrusted local certificates, worker-response bypass, removal of cookies/local storage/worker registrations after profile reopen, byte-for-byte preservation of protected Vault data and existing redirect/frame policy regressions. Cleanup testing also exposed a shutdown access to the already-disposed original controller; disposal now operates only on retained tabs. Remaining work includes file pickers, full worker/connection attribution, hostile-frame testing, runtime-update failure scenarios, account-provider compatibility and independent security review. This checkpoint does not certify sensitive-account readiness.

## References

- [Microsoft: Develop secure WebView2 apps](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
- [Microsoft: Server certificate errors](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.servercertificateerrordetected)
- [Microsoft: WebView2 distribution and updates](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- API settings and browsing-data semantics were also checked against the pinned WebView2 SDK XML reference.
