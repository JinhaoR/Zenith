# ADR 0012: Document request gate and independent network observations

Date: 2026-09-08

Status: Accepted development checkpoint

## Evidence

The earlier synthetic-response tests could observe a requested URL even after
navigation cancellation, but could not prove that a server received an HTTP
request. The new loopback harness uses separate TCP listeners for allowed,
Greylisted and Blacklisted destinations, temporary WebView2 profiles, ephemeral
ports and browser-local test hostname mapping. It does not modify Windows hosts,
DNS or proxy settings. Positive controls verify that the observers work.

Before this change, the Greylisted server recorded two denied redirect requests
(single-hop and multi-hop) and a denied script-navigation request. The native
boundary was still shown. The same assertions pass after this change: those
denied HTTP requests are absent, while allowed redirects and embedded widgets
reach their servers. This is actual HTTP observation, not a claim that all DNS,
connection, cache or worker activity is intercepted.

## Decision

Install `DocumentRequestGuard` on every tab before it is ready. It uses WebView2's
host-side Chrome DevTools Protocol API to enable `Fetch` interception at the
request stage for `Document` resources. Each intercepted request remains paused
until Core permits it or the host fails it with `BlockedByClient`. Redirect hops
receive separate checks. This does not open a remote-debugging port or expose a
website-to-host policy API.

The guard obtains the main frame ID from `Page.getFrameTree`, observes committed
main-frame navigation and distinguishes main versus embedded documents by CDP
frame identity. It must not reuse the ad filter's URL/referrer heuristic as an
access-policy authority. Existing NavigationStarting checks and native policy
surfaces remain defense in depth and own the user-facing navigation flow.

Core's `DocumentRequestPolicy` reuses the navigation evaluator. Main documents
require independent authorization. Embedded HTTP(S) documents require a currently
authorized top-level document. A Whitelisted top-level document permits otherwise
Greylisted frames as already agreed by the user; Blacklist, unsupported targets
and policy failures remain denied. A temporary Greylist page does not implicitly
authorize another Greylisted hostname. Core tests cover expiry and failure paths.

Protocol calls have ten-second deadlines. Failed initialization never marks the
tab ready. Runtime protocol/metadata failure stops loading and closes the browser
window on its dispatcher, disposing its controllers. This deliberately favors
denial over keeping a possibly unprotected renderer alive. Disposal never calls
`Fetch.disable`, since disabling interception could release pending requests.

## Verification

The real renderer regression now includes server-side assertions for direct
denial, single/multi-hop redirects, script navigation, links, POST forms, denied popups, allowed
redirects, allowed nested widgets, Blacklisted frames and new-tab redirects.
A simulated channel failure verifies window teardown. Existing synthetic-page
tests still cover settings, Vault, temporary access, bookmarks and ad/cosmetic
filtering. The loopback harness is part of the opt-in `ZENITH_WEBVIEW_TESTS=1`
suite and makes no Internet requests for its test pages.

## Remaining work

This checkpoint does not complete Phase 5. Service workers, WebSockets, cached
and inherited documents, speculative connections, out-of-process target coverage,
already-open connections and complete request cancellation on grant expiry need
their own tests and hardening. Loopback HTTP observations do not prove universal
zero network contact or HTTPS/platform-wide coverage. Recovery and stronger
offline time/snapshot tamper resistance remain separate decisions.

References: [Fetch protocol](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/),
[WebView2 CDP integration](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/chromium-devtools-protocol).
