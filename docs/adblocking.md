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

The mandatory host layer remains independent of the resource-filtering layer below. A Whitelist entry never overrides mandatory host filtering. Browser caches, service workers, WebSockets and independent network-observation coverage require further hardening before claiming universal network isolation.

## 7. Network and Cosmetic Filtering Checkpoint

Zenith runs the pinned Ghostery engine 2.18.2 in a host-side ClearScript V8 runtime with no exposed CLR objects. The engine is bundled with the application, not downloaded at runtime or injected into websites. EasyList and EasyPrivacy are the fixed data subscriptions:

- `https://easylist.to/easylist/easylist.txt`
- `https://easylist.to/easylist/easyprivacy.txt`

No social-blocking, annoyance or cookie subscription is enabled. Resource-list exceptions apply only to advertising/tracking rules, never to Blacklist or navigation policy. Main-document access remains owned by navigation policy; resource rules do not reclassify sites. Intercepted ad requests receive an empty local 403 with a distinct resource-filter reason.

Network matching supports the maintained engine's URL patterns, regular expressions, resource types, domain restrictions, first/third-party matching and exceptions. Request context uses the browser-supplied Referer when available, otherwise the top-level URL. WebView2 does not expose a request frame ID and may omit Sec-Fetch-Dest; document requests are compared with the normalized native main-navigation target to distinguish frames. Same-URL frame/main requests and missing frame/referrer metadata are an explicit limitation, not an access-policy authority.

The separate site-policy document gate uses CDP frame IDs and Core decisions
(ADR 0012). The resource filter's metadata heuristic is not used to grant site
access. It remains a limitation for ad-rule matching context only.

Cosmetic filtering applies ordinary CSS hiding selectors and exceptions in HTTP(S) documents and nested frames, including dynamically inserted elements. Each frame requests rules using its actual WebView2 message-source URL. A fixed application script applies CSS through a constructed stylesheet without weakening the page's CSP. Its observer processes 500 elements per tick, queues at most 64 subtree walks, and collects at most 512 classes, 512 IDs and 128 link values per document. Tokens and input/output sizes are bounded; the native bridge limits both per-frame and per-tab work. Large documents may exceed these discovery limits. Page navigation resets the stylesheet and DOM hints; destroyed frames release native subscriptions without calling APIs on the destroyed frame.

Only declarative hiding is enabled. Scriptlets, custom style actions, extended/procedural actions, response/HTML rewriting, CSP modification and resource replacement scripts are excluded. The lists never supply executable JavaScript. Network redirects/rewrite outputs are not followed; a matched blocking rule remains blocked. Query-parameter rewrite outputs are currently ignored.

### Updates and failure handling

The two resource lists are one atomic snapshot, independent of the mandatory hosts cache. Unmodified bundled snapshots provide initial/offline resource filtering. They do not waive the mandatory Blacklist's first-download requirement. A valid saved resource snapshot takes precedence over bundled data; a corrupt/uncompilable resource cache falls back to the bundle, without changing site policy.

Updates are checked daily while running and failed updates retried hourly. Downloads use fixed HTTPS URLs with redirects disabled, a 90-second combined deadline and a 16 MiB cap per list. Validation checks headers, UTF-8, line lengths, rule counts, compiled network/cosmetic counts and unexpected shrinkage. Both lists must validate and compile before the protected cache is atomically replaced and the engine published. Failed download, compilation or persistence retains the previous snapshot. No previous protected envelope is automatically restored. Network matching failures block the affected request; a cosmetic failure leaves network and site protection active. If no engine can load, intercepted subresources fail closed.

New network rules apply to subsequent intercepted requests. Existing cosmetic sheets are refreshed on later DOM queries; reload pages after an update for consistent rule/exception application. Settings → About exposes the engine version, list versions, update time, compiled counts, snapshot hash, aggregate blocked count and failure status. It does not record browsing URLs or expose a policy bypass.

### Remaining limitations and maintenance

This is not full uBlock Origin equivalence. Scriptlets, anti-adblock responses, same-origin video ads, shadow DOM, inherited blank/srcdoc cosmetics, complete cache/worker/WebSocket coverage and exact initiating-frame metadata need further work. Cosmetic CSS runs in the page's renderer and can be removed or interfered with by a hostile page; it is not a security boundary. Blacklist enforcement remains native and independent.

For a broken site, first distinguish a native site-policy boundary, a mandatory host response and an ad-resource response. Check Settings → About for failed updates and reload after a successful update. Report reproducible public filter-list issues to the EasyList maintainers; never include private URLs, tokens or account data. Upstream filter exceptions can repair resource-list false positives without overriding mandatory Blacklist. A Blacklist false positive needs correction in its upstream source, not a Vault exception.

Engine updates require a reviewed application release and a reproducible bundle rebuild; list updates do not. See ADR 0011 and `tools/adblock/README.md` for dependencies, licenses and build instructions.
