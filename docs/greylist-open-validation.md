# Greylist open shutdown: cause and regression

Date: 2026-09-19. Native runtime: 153.0.4234.32.

The reported destination was youtube.com. An isolated live reproduction granted
only that hostname. Its redirect to www.youtube.com was correctly denied by Core.
Native NavigationStarting cancelled the redirect while MainWindow scheduled
about:blank to clear the old document. A late Fetch.requestPaused callback then
attempted a command for the cancelled request; the guard's generic error path
treated it as a broken interception channel and requested application shutdown.
Granting both hosts in the diagnostic fixture did not reproduce that path.

DocumentRequestGuard now correlates Fetch.networkId with the terminal native CDP
Network.loadingFailed requestId and canceled flag for Document requests. A late
event or failing command for that confirmed cancelled request does not require
another cancellation or channel shutdown. No request is resumed by this path.
Uncorrelated failures still fail closed. The bounded cache retains 256 recent
opaque cancellation IDs, with no URLs, cookies or request bodies.

This uses the documented correlation fields in the Chromium protocol:
[Fetch.requestPaused](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/#event-requestPaused)
and [Network.loadingFailed](https://chromedevtools.github.io/devtools-protocol/tot/Network/#event-loadingFailed).
Native/Core navigation checks and the fail-safe clearing lifecycle remain active.
This does not introduce a www alias or expand a Greylist grant to subdomains.

The new GreylistOpenScenario runs the actual MainWindow and access dialog with
an isolated protected store and independent loopback servers. It verifies:

- No password fields or initial password setup in cooldown-only mode.
- No navigation before the wait and explicit confirmation.
- Twenty-one cross-host denied redirects, completed blank clearing and a live
  window/controller that can navigate again afterward.
- No denied destination request observed by the loopback server in this scenario.
- Grant expiry still clears the retained document.
- Optional password setup through the real Vault UI waits before taking effect.

The same isolated live YouTube redirect completed cancellation/blank clearing
without the previous guard failure after the fix. This is a signed-out regression,
not an authenticated YouTube compatibility certification. A request for youtube.com
still cannot silently authorize www.youtube.com; request the final hostname if it
is Greylisted. Cooldown and visit durations were not changed.

Run the focused native scenario with ZENITH_WEBVIEW_TESTS=1 and
ZENITH_GREYLIST_TESTS_ONLY=1, using the BrowserSettingsAndGreylistFlow test filter.
The full suite is also run with ZENITH_WEBVIEW_TESTS=1; results and any limits are
reported with the change.

Final verification: restore and solution build succeeded with zero warnings/errors.
With native WebView2 tests enabled, 265 Core tests and 89 App tests passed; three
separately opted-in research/download tests were skipped. Native coverage included
the F02 OOPIF redirect checks, F06 chooser checks, all document-clearing failure
paths, normal navigation/popups, authentication fixtures and both access modes.
The existing background-tab permission test was adjusted to resume execution,
reset its fixture permission and reveal a deferred request: runtime 153 can defer
geolocation while hidden. It checks no hidden success and native denial after
activation; production capability behavior was not changed.
