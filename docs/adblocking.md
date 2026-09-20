# Ad Blocking

## 1. Role

Ad blocking supports Zenith but does not define its access model.

Site classification determines whether navigation is allowed. Ad blocking determines which network resources or page elements may be loaded within an allowed page. These systems must remain separate.

## 2. List Types

Zenith may consume two distinct kinds of external data:

- **Site lists**, which can contribute entries to the Blacklist.
- **Resource filter lists**, which block advertising, tracking or unwanted subresources.

A resource filter rule must not silently reclassify a site, and a failed filter update must not weaken site-access policy.

## 3. Update Model

External lists should be updated periodically through a replaceable provider layer.

Updates must:

- Use identified and reviewable sources.
- Be downloaded as data, never executed as code.
- Be validated before activation.
- Be applied atomically.
- Retain the last-known-good version when an update fails.
- Expose source, version, update time and failure status for diagnostics.

The application should not require a new Zenith release whenever a filter rule changes.

## 4. Filtering Scope

The initial implementation should prioritize network-request filtering. Cosmetic filtering and site-specific scriptlets may be added only after their maintenance and security costs are understood.

Highly dynamic sites, including video platforms, should be handled through updateable rules rather than hard-coded application logic.

## 5. User Control

The current mandatory Blacklist source and category selection are fixed application policy, explicitly requested by the user. They cannot be edited, disabled or overridden in the Vault. Any future optional resource-filter sources require a separate policy decision; they cannot override the mandatory Blacklist.

Zenith should explain whether a failure came from site policy, a Blacklist source or a resource filter; these are different decisions.

## 6. Implemented Host-filtering Checkpoint

Zenith synchronizes the StevenBlack **unified hosts + fakenews + gambling + porn** variant, excluding the social extension, from the fixed HTTPS URL in ADR 0010. The native updater runs independently of web content, checks daily while Zenith runs, and retries failures hourly. It has no user-provided URLs and accepts no redirects. Updates are bounded, parsed as data and saved atomically before publication. The last valid protected cache survives offline restarts and failed updates. Without a valid cache or successful first download, browser navigation and intercepted resource requests fail closed.

The immutable host index matches canonical **exact hostnames**, as a hosts file does. It does not infer wildcard/subdomain blocks. The index contributes authoritative Blacklist decisions to site policy and is also checked for all resource contexts/source kinds exposed by WebView2's request interception. Listed HTTP(S) page, frame, image and fetch requests receive a local empty 403 response; unlisted resources are not classified as navigation merely because they support a page. The implementation does not modify Windows' system hosts file.

Settings → About exposes source/category information, last-success time, count, hash and failure status without controls that weaken protection. Changes to the blocked-host set unload retained pages/frame trees and refresh Sphere discovery; comment-only changes do not interrupt browsing. Users must reopen destinations after an effective list update.

The mandatory host layer remains independent of the resource-filtering layer below. A Whitelist entry never overrides mandatory host filtering where applied. Resource filtering is additional protection within its implemented coverage, not a promise of universal network isolation. Ordinary backend communication by an authorized website is not a navigation violation; see `threat-model.md` for the clarified scope.

## 7. Network and Cosmetic Filtering Checkpoint

Transport restrictions precede advertisement exceptions: intercepted public HTTP/WS resources are denied even when a filter would allow them. Mandatory Blacklist matching remains authoritative. Native shared/service-worker sources are denied separately by the capability safety guard, not evaluated with the selected page's filter context. Document-scoped requests, including dedicated workers, continue through the resource engine. See ADR 0016 for worker cleanup, native regressions and remaining coverage limits.

Zenith runs the pinned Ghostery engine 2.18.2 in a host-side ClearScript V8 runtime with no exposed CLR objects. The engine is bundled with the application, not downloaded at runtime or injected into websites. EasyList and EasyPrivacy are the fixed data subscriptions:

- `https://easylist.to/easylist/easylist.txt`
- `https://easylist.to/easylist/easyprivacy.txt`

No social-blocking, annoyance or cookie subscription is enabled. Resource-list exceptions apply only to advertising/tracking rules, never to Blacklist or navigation policy. Main-document access remains owned by navigation policy; resource rules do not reclassify sites. Intercepted ad requests receive an empty local 403 with a distinct resource-filter reason.

Network matching supports the maintained engine's URL patterns, regular expressions, resource types, domain restrictions, first/third-party matching and exceptions. Request context uses the browser-supplied Referer when available, otherwise the top-level URL. WebView2 does not expose a request frame ID and may omit Sec-Fetch-Dest; document requests are compared with the normalized native main-navigation target to distinguish frames. Same-URL frame/main requests and missing frame/referrer metadata are an explicit limitation, not an access-policy authority.

The separate site-policy document gate uses CDP frame IDs and Core decisions
(ADR 0012). The resource filter's metadata heuristic is not used to grant site
access. It remains a limitation for ad-rule matching context only.

Cosmetic filtering applies ordinary CSS hiding selectors and exceptions in HTTP(S) documents and nested frames, including dynamically inserted elements. Each frame requests rules using its actual WebView2 message-source URL. A fixed application script applies CSS through a constructed stylesheet without weakening the page's CSP. Its observer processes 500 elements per tick, queues at most 64 subtree walks, and collects at most 512 classes, 512 IDs and 128 link values per document. Tokens and input/output sizes are bounded; the native bridge limits both per-frame and per-tab work. Large documents may exceed these discovery limits. Page navigation resets the stylesheet and DOM hints; destroyed frames release native subscriptions without calling APIs on the destroyed frame.

In this Ghostery adapter, only declarative hiding is enabled. Scriptlets, custom style actions, extended/procedural actions, response/HTML rewriting, CSP modification and resource replacement scripts are excluded. Its lists never supply executable JavaScript. Network redirects/rewrite outputs are not followed; a matched blocking rule remains blocked. Query-parameter rewrite outputs are currently ignored. The separately bundled uBO Lite extension has its own executable content-filtering functionality; see section 9.

### Updates and failure handling

Core converts exceptions from the mandatory-list provider or resource engine into an unavailable/denied resource decision. The native adapter also catches metadata and response failures: it assigns a local 403, or requests controller teardown if assignment itself is unavailable. Allowed requests are not assigned a temporary response. A loopback regression injects an engine exception and verifies both the local denial and absence of a server request.

The two resource lists are one atomic snapshot, independent of the mandatory hosts cache. Unmodified bundled snapshots provide initial/offline resource filtering. They do not waive the mandatory Blacklist's first-download requirement. A valid saved resource snapshot takes precedence over bundled data; a corrupt/uncompilable resource cache falls back to the bundle, without changing site policy.

Updates are checked daily while running and failed updates retried hourly. Downloads use fixed HTTPS URLs with redirects disabled, a 90-second combined deadline and a 16 MiB cap per list. Validation checks headers, UTF-8, line lengths, rule counts, compiled network/cosmetic counts and unexpected shrinkage. Both lists must validate and compile before the protected cache is atomically replaced and the engine published. Failed download, compilation or persistence retains the previous snapshot. No previous protected envelope is automatically restored. Network matching failures block the affected request; a cosmetic failure leaves network and site protection active. If no engine can load, intercepted subresources fail closed.

New network rules apply to subsequent intercepted requests. Existing cosmetic sheets are refreshed on later DOM queries; reload pages after an update for consistent rule/exception application. Settings → About exposes the engine version, list versions, update time, compiled counts, snapshot hash, aggregate blocked count and failure status. It does not record browsing URLs or expose a policy bypass.

### Remaining limitations and maintenance

Connection acceptance testing has confirmed denied WebSocket handshake contact
on WebView2 152.0.4191.66. The Websocket resource-context branch is not sufficient
evidence of native interception. Under the clarified product model this is an
Informational filtering limitation, not an account-security release blocker.
SEC-CONN-001 and the historical strict failing gate are
documented in `connection-coverage.md`; mandatory-host enforcement must not be
claimed across this unguarded path until it is fixed and retested.

This is not full uBlock Origin equivalence. Scriptlets, anti-adblock responses, same-origin video ads, shadow DOM, inherited blank/srcdoc cosmetics, complete cache/worker/WebSocket coverage and exact initiating-frame metadata need further work. Cosmetic CSS runs in the page's renderer and can be removed or interfered with by a hostile page; it is not a security boundary. Blacklist enforcement remains native and independent.

For a broken site, first distinguish a native site-policy boundary, a mandatory host response and an ad-resource response. Check Settings → About for failed updates and reload after a successful update. Report reproducible public filter-list issues to the EasyList maintainers; never include private URLs, tokens or account data. Upstream filter exceptions can repair resource-list false positives without overriding mandatory Blacklist. A Blacklist false positive needs correction in its upstream source, not a Vault exception.

Engine updates require a reviewed application release and a reproducible bundle rebuild; list updates do not. See ADR 0011 and `tools/adblock/README.md` for dependencies, licenses and build instructions.

## 8. Optional extension status in Settings

Settings → Content Protection is an App-only, read-only snapshot of uBlock Origin
Lite metadata from the active WebView2 profile. `ContentProtectionReader` uses
[`GetBrowserExtensionsAsync`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2profile.getbrowserextensionsasync)
on each refresh. It recognizes the exact browser-reported name `uBlock Origin Lite`
for presentation only: a name is not a verified package identity, signature or
authority to install or execute code. Duplicate matching records or invalid
metadata are Unavailable. A single result displays Enabled or Disabled using the
native flag; this does not prove that filtering is working on a particular page.
No matching record displays Not installed in the current browser profile. An
unready/disposed controller, API failure or five-second timeout displays
Unavailable. Closing Settings during a refresh discards the late UI update.

The native [`CoreWebView2BrowserExtension`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2browserextension)
metadata provides ID, name and enabled state, but no version. Zenith does not read
private Chromium profile files or query extension JavaScript to guess a version.
Extension settings are not exposed: the raw experiment's `chrome-extension://`
dashboard route would need a separately reviewed native integration; ordinary
site navigation continues to reject that scheme.

Production now enables extensions and installs the verified built-in uBO Lite
package before browsing becomes ready (section 9). It offers no arbitrary
extension management. An installation in the isolated experiment's user-data
folder is not an installation in Zenith's profile.
Microsoft documents that extension enumeration is empty when extension support is
disabled; this view describes what the current environment exposes, not packages
in other profiles or inactive private storage. Existing host/resource filtering
and Core site policy remain independent.

Validation includes enabled/disabled/missing/ambiguous metadata, errors and timeout
unit tests, plus native WebView2 installation, enable/disable, removal and Settings
refresh/close tests using an isolated, permission-free MV3 metadata fixture. The
fixture is not uBO Lite and makes no filtering claim. The separate actual-extension
compatibility evidence remains in `ubolite-webview2-experiment.md`.

## 9. Built-in uBO Lite

Zenith now bundles a pinned official Edge uBO Lite package using supported
WebView2 extension APIs. This adds trusted upstream executable content filtering,
including packaged scriptlets and resource replacements, alongside the existing
Ghostery adapter. The data-only, last-known-good list-update contract above still
applies to Zenith's own subscriptions; it is not an extension update guarantee.
No imported lists, new dashboard route, or custom engine is added.

Core and the existing native guards retain all site-access and capability
decisions. uBO may add content/document denials, but cannot grant a Core-denied
navigation. Full interception of extension background traffic is not promised.
Package lifecycle, permissions, persistence, update limitations and current
validation are defined in [browser-extensions.md](browser-extensions.md) and
[ADR 0021](decisions/0021-bundled-webview2-extensions.md).
