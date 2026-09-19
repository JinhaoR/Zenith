# uBO Lite in WebView2: isolated experiment

Date: 2026-09-14. Status: technically viable pilot; not an adopted architecture.

**uBO Lite works for useful content filtering in Zenith's current WebView2 environment.** Both official Edge and Chromium packages loaded. Packaged network rules, cosmetic filters and scriptlets worked, including alongside the existing Zenith guard adapters. This does not establish complete MV3 compatibility, authenticated-site compatibility, or safety of a production extension integration.

Production code, Core policy and navigation enforcement were not changed by this experiment. Earlier security-task changes already in the working tree were preserved. The standalone [harness and reproduction instructions](../tools/experiments/ubolite/README.md) and [raw evidence](security-evidence/ubolite/) are the deliverables, alongside this assessment.

## Environment and method

- Windows WPF; Zenith WebView2 SDK 1.0.4129.50; installed runtime 152.0.4191.66.
- Official uBO Lite 2026.907.2003 Edge and Chromium unpacked packages. The previous Edge package, 2026.901.1442, was used for an upgrade test. [Official release](https://github.com/uBlockOrigin/uBOL-home/releases/tag/2026.907.2003).
- Separate disposable user data folders; no personal profiles, account credentials, file selection or public posting.
- Local HTTP fixtures with independently recorded server requests, DOM observations, extension API probes, disable/re-enable controls and separate anonymous public-site smoke tests.
- Chromium sandboxing, origin isolation and certificate validation were retained. The harness selected direct networking with --no-proxy-server for reproducibility; this is not a proposed production change.

Archive SHA-256 values:

| Package | SHA-256 |
| --- | --- |
| Current Chromium | 56BA6FB728CC272BCA1931792B1A4A15963EEDE4557EA829E5367E85D6057DD9 |
| Current Edge | 1FD67CAD123BF677AA23CC7E1EFB524B219EFAABCF586F211843850662B9B1D2 |
| Previous Edge | 061A5DE7B1EEDF6C1FE0AFDA40D453C427EEFC2CBCDBB680C3EED37DC5CEF2C8 |

Downloaded packages and profiles remain in ignored build output. Evidence contains synthetic state and public-page measurements. An exit code of zero means the harness finished, not that every probe passed. Earlier chromium.json used a nonmatching generic selector; use chromium-final.json for the corrected cosmetic result.

## Supported host integration and persistence

Set AreBrowserExtensionsEnabled on CoreWebView2EnvironmentOptions before environment creation. It defaults to false; controllers sharing an environment/user data folder need consistent options. This is an environment decision, not a per-page switch. [Microsoft environment options](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2environmentoptions).

AddBrowserExtensionAsync accepts an unpacked manifest directory and installs into the current profile, enabled immediately. Installation persists, but the extension's files are not copied into the user data folder. Microsoft warns that modifying installed extension content can remove it; adding the same path again reinstalls it. Consequently, temporary download folders and in-place live updates are unsuitable. [Microsoft installation contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2profile.addbrowserextensionasync).

Observed results:

| Probe | Result and evidence |
| --- | --- |
| Extension support disabled | Add failed with 0x80070032, ERROR_NOT_SUPPORTED; extensions-off.json |
| Current Edge and Chromium builds | Both installed and filtered; edge-final.json, chromium-final.json |
| Restart without another Add call | Enabled state, mode, synthetic storage marker and filtering persisted; restart.json |
| Native disable and re-enable | Disable plus reload restored blocked local requests and hidden content; re-enable succeeded |
| Package update at stable path | Previous to current release retained ID, mode and marker; update-before.json, update-after.json, update-verified.json |
| WebView2 runtime update | **Not tested:** only one runtime version was installed |

The native extension object exposes ID, name, enabled state, EnableAsync and RemoveAsync. Removal was not separately exercised here. [Microsoft extension API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2browserextension).

Enumeration also returned Microsoft's built-in Clipboard and PDF component extensions, even with host extension installation disabled. Never identify uBO by selecting the first extension or remove every unfamiliar component. The harness's name lookup is convenient only in its controlled profiles; production must validate the expected identity and package.

Observed registration resides in UDF/EBWebView/Default/Secure Preferences. State also uses Local Extension Settings/ID, DNR Extension Rules, Extension Rules, Extension Scripts, Extension State and Service Worker storage. Extension code remains at the installation path. Neither package declares a manifest key; changing the unpacked path produced a different identity. A stable, protected installation path or a separately reviewed stable-identity packaging strategy is necessary. Profile backup/reset semantics must include extension settings without exposing authenticated browser data.

The upgrade experiment closed the browser, replaced the package at its stable experiment path, called Add again, and restarted. The final extension-reported manifest version was 2026.907.2003 with the original marker. This is evidence for that procedure, not an atomic production updater or runtime-update guarantee.

## Filtering and MV3 compatibility

Both manifests declare MV3, minimum Chromium 122 and a module service worker. Both targets worked; use the official **Edge target for a Windows pilot**, pinning one reviewed artifact. No measured advantage over the Chromium target was established. Upstream supports both build targets. [Upstream build instructions](https://github.com/gorhill/uBlock/blob/master/platform/mv3/README.md).

| Feature | Experimental result |
| --- | --- |
| Packaged DNR rules | Worked. Matching requests absent from independent local server logs; control requests arrived |
| EasyList-style blocking | Packaged /xpopup/xpopup.js rule blocked; disable restored delivery |
| EasyPrivacy-style blocking | Packaged /analytics/rakuten.js rule blocked; disable restored delivery |
| Generic and specific cosmetics | Complete mode hid matching generic and packaged specific test elements; control remained visible |
| Packaged scriptlets | Test sentinel changed as specified by the bundled scriptlet; disabling restored ordinary page behavior |
| Basic mode | Network blocking worked; cosmetic/scriptlet content scripts were absent, as expected |
| Fresh stock installation | Default Optimal mode was 2; network filtering worked. Complete-mode generic filtering should not be assumed at that default |
| Imported network list | Local list compiled into dynamic rules and blocked its test request |
| Imported network-list update | Simulated expiry and changed server list replaced the dynamic matching rule |
| Imported cosmetic rule | Tested plain imported selector did not apply |
| Manually added cosmetic selector | Dashboard custom CSS route worked in inspect.json |
| Custom scriptlet | Did not execute; chrome.userScripts was undefined and upstream detected no support |

Evidence: edge-final.json, chromium-final.json, basic.json, inspect.json and the disable controls within the complete runs. Tests establish representative functionality, not correctness of every list rule or scriptlet. Packaged test rules were deliberately enabled for deterministic local tests; they are not a proposed shipped configuration.

The declared userScripts permission alone did not expose its API. Chromium requires an additional user-controlled gate; the normal extension-management UI was unavailable here, and no supported WebView2 API for enabling that gate was identified. Do not bypass this with private preferences, developer flags or weakened browser security. Packaged scripting worked through a different API, so this is not a blanket failure of extension scripts. [Chromium userScripts requirements](https://developer.chrome.com/docs/extensions/reference/api/userScripts).

Current uBO Lite supports imported lists; older statements that Lite can never import lists are obsolete for this release. Consult the dated changes rather than extrapolating from older FAQ passages. [Upstream changelog](https://github.com/uBlockOrigin/uBOL-home/blob/main/CHANGELOG.md).

There is an important failure case: after restart, an imported local list's original server was unavailable. Forcing its expiry removed its custom dynamic rule instead of retaining it. This is a narrow reproduced case, not proof of every update-failure path. It nevertheless prevents claiming equivalence to Zenith's documented last-known-good resource-list guarantee without further work or an explicit product decision.

The packages have no update_url. runtime.requestUpdateCheck reported no_update. Store-driven extension updates were not demonstrated: plan for host-managed package updates. Bundled rules travel with packages; remote imported data lists are a separate update mechanism with separate failure behavior.

## Settings without browser chrome

Manually opening chrome-extension://ID/dashboard.html worked. Opening edge://extensions failed. Opening popup.html directly rendered a page but lacked useful current-site context; querying the active extension tab returned no usable website URL. A popup opened as a normal document is not a substitute for an Edge toolbar action. [WebView2 browser-feature differences](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/browser-features).

The minimal future host UI should show installed/enabled/version/update health, a user-controlled cleanup toggle, and a Settings action. It should distinguish filtering failures from Core denials. A per-site control must derive the selected site from trusted native tab state, not accept an arbitrary website-supplied target.

Zenith's existing policy correctly cancels the extension dashboard URL when opened as ordinary website navigation. A future settings surface therefore needs a narrowly scoped host-owned route for the verified extension's dashboard and required resources. Do not allow all chrome-extension URLs globally or expose Vault/native objects there. The harness used internal upstream dashboard messages for reproducible configuration; these are not stable supported integration APIs. A production adapter would require versioning and tests, or upstream cooperation.

## Interaction with Zenith and the trust boundary

The zenith and combined modes attached the actual current NetworkSafetyGuard, DocumentRequestGuard, ResourceRequestGuard and BrowserCapabilityGuard. Combined also attached AdblockService and CosmeticFilterGuard. Allowed literal-loopback fixture documents loaded; packaged EasyList/EasyPrivacy rules and generic cosmetics continued working. Core still cancelled denied HTTP localhost navigation and ordinary extension-dashboard navigation. See zenith-final.json and combined-final.json.

HTTP localhost was deliberately replaced by literal loopback for these fixture runs because Core's existing transport policy does not exempt the hostname. Packaged localhost-specific scriptlet tests are consequently not applicable to the literal-IP fixture; their absence is not evidence of a guard conflict.

These tests establish adapter coexistence, **not** complete MainWindow integration or exhaustive extension-created navigation coverage. Website service-worker restrictions were not relaxed. Do not remove them merely because uBO has an extension background worker. Imported-list update behavior under all existing guards remains a separate acceptance case.

The current package has all-URLs host access, scripting, activeTab, storage, unlimitedStorage, alarms, declarativeNetRequest, offscreen and userScripts permissions. This is powerful access: an extension with page scripting can read or alter sensitive authenticated page content, even without a cookies API permission. The installed package, its maintainer and its update supply chain become trusted content-processing components. They must not become Core policy authorities. [Upstream permission rationale](https://github.com/uBlockOrigin/uBOL-home/wiki/Justification-for-the-declared-permissions).

The reviewed manifest has no externally_connectable entry, and no onMessageExternal handler was found. Internal privileged dashboard messages check the extension origin; content-script interactions also exist. These observations are not a complete extension security audit. DOM interactions, injected scripts and web-accessible extension resources still form boundaries worth testing. Do not add a website-to-extension-to-WPF command bridge.

No TLS interception, certificate exception or sandbox change is required. Browser origin separation and normal cookies remain in Chromium, but extension host privileges intentionally permit access beyond ordinary page-origin privileges. Keeping credentials out of native logs does not protect them from a compromised all-URLs extension. Microsoft's host guidance still applies: validate sources and keep privileged host functionality away from web content. [Microsoft WebView2 security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).

uBO's observed strictBlockMode and popupBlockMode were enabled. Those features can add document denials or extension interstitials. To maintain the requested responsibility split, a production proposal must configure their scope deliberately and test resulting redirects/popups. An extension exception must never grant a Core-denied navigation. Do not silently map uBO lists or exceptions into Whitelist/Greylist/Blacklist.

Current adblocking.md specifies data-only list updates, excludes scriptlets and promises last-known-good resource updates. Adoption would therefore require a reviewed specification change distinguishing executable extension-package updates from filter data, accepting selected packaged scriptlets and defining imported-list failures. This experiment does not change those requirements.

## Performance and public-site smoke results

Single ordered samples, separate profiles, anonymous pages. Timings below are navigation loadEventEnd, not statistically established overhead. Resource counts come from Resource Timing and are not independent packet counts. No account login, MFA, authenticated Gmail workflow or video-ad suppression was validated.

| Site | Baseline / uBO load ms | Observation |
| --- | --- | --- |
| Wikipedia | 457 / 585 | Public article content loaded; 38 / 38 resource entries |
| GitHub | 496 / 785 | Public homepage loaded; 147 / 148 entries |
| Gmail | 1062 / 1253 | Signed-out landing only; 20 / 20 entries |
| YouTube | 2853 / 3412 | Separate ten-second samples reached complete state and one video element; no playback test |
| Reddit | Not comparable | Both baseline and extension navigation failed; compatibility unresolved |
| Speedtest, advertisement-heavy example | 793 / 918 | Landing loaded; 34 / 31 entries; speed test not started |
| CNN, tracker-heavy example | 1372 / 1293 | Content loaded; 96 / 80 entries |

Evidence: baseline.json, edge-final.json, youtube-baseline.json and inspect.json. Early YouTube samples were still loading and are not used as completed load measurements. Network variability, consent, regional responses and caching prevent causal speed claims or advertising-coverage guarantees.

Fresh environment initialization was approximately 354–376 ms across baseline/extension samples, **before** extension installation. Initial Add cost approximately 422–439 ms. This does not measure the time until every extension rule is ready.

A separate stock Optimal-mode sample, without dashboard or imported-list setup, waited 35 seconds for idle. WebView2 subprocess totals were:

| Metric | No extension | Stock uBO | Difference |
| --- | --- | --- | --- |
| Working set | 315 MiB | 352 MiB | about 37 MiB |
| Private bytes | 148 MiB | 179 MiB | about 31 MiB |
| Processes after idle | 5 | 5 | 0 |

See measure-baseline.json and measure-stock.json. This is one pair, excludes the WPF host and can double-count shared working pages. Configuration/import compilation had higher transient memory; do not describe that as idle overhead. No demonstrated total-memory saving versus Zenith's current Ghostery integration is claimed.

## Alternatives

| Option | Maintenance and customization | WebView2 suitability |
| --- | --- | --- |
| uBO Lite | Upstream maintains MV3 rules/scriptlets; host still owns package lifecycle and settings wrapper. Imported networking worked, some custom scripting/cosmetics did not | Best supported by this experiment; conditional pilot |
| AdGuard MV3 | Maintained MV3 product with custom-filter support; its API/permission footprint needs independent evaluation | Plausible alternative, not installed or benchmarked here |
| No built-in blocker | Lowest extension/update/UI burden; less cleanup and tracker blocking | Valid access-policy architecture, but removes a currently provided filtering feature if applied to Zenith |
| Zenith-specific engine | Maximum control, highest matching/scriptlet/update maintenance | Do not build a new engine. Existing Ghostery integration is a maintained-library approach, not a reason to invent another |

AdGuard comparison is desk research only. Do not assume its custom features survive WebView2 merely because they work in Chrome. [AdGuard source](https://github.com/AdguardTeam/AdguardBrowserExtension), [MV3 custom-filter changes](https://adguard.com/en/blog/adguard-browser-extension-v5-2.html).

## Recommended architecture and adoption gates

Proceed with an **optional uBO Lite pilot**, outside Core. Do not make it official or bundle it by default yet. Once the gates below pass, a reviewed, bundled, default-enabled but user-disableable cleanup component is reasonable. Basic versus Optimal versus Complete should be an explicit product choice; preserve the upstream Optimal starting point for compatibility testing rather than silently escalating every site to Complete.

Required future changes, none implemented here:

1. App-owned extension lifecycle: consistent environment option, per-profile installation/reconciliation and verified artifact identity. Install only the bundled approved package from a protected path; no arbitrary folder chooser, website installation command or store surface. Do not identify by display name.
2. Native status/settings wrapper with a narrowly authorized extension dashboard surface and no privileged host bridge. Keep access policy and capability decisions in Core; disabling cleanup must not disable mandatory Blacklist enforcement.
3. Host-owned package updater: verified version/hash provenance, staging, closed-controller activation, identity/version/filter-health checks and tested rollback. Retain a known usable package on failure and report cleanup health independently. Do not silently relax Core on extension failure. Respect upstream GPLv3-or-later distribution obligations.
4. Deliberate configuration for document blocking, popups, imported lists and scriptlets. Do not promise unsupported custom-scriptlet features or work around them by weakening Chromium.
5. If adopted, replace the existing advertising resource engine and cosmetic bridge in a separately reviewed change; avoid permanent double filtering. Preserve mandatory host checks, transport/capability guards and native document policy enforcement. No uBO matching logic belongs in Core.

Before official adoption, validate:

- An actual WebView2 runtime transition, including supported older/current candidates and extension restart state. Persistence across the tested package update is not a substitute.
- Full native Zenith F02/F03/F06 regressions with the extension, including extension redirects/interstitials, tab creation, popups, frames, settings navigation and document teardown failures.
- Dashboard/source-boundary isolation and extension-specific navigation; no website route into privileged native commands.
- A production updater's interrupted activation, tampered package, stable identity, rollback and state migration.
- Imported-list offline/invalid-update behavior against the agreed product guarantee.
- Disposable-account authentication, MFA, uploads and ordinary workflows for the named services; resolve Reddit's baseline failure. Do not use primary accounts to perform first validation.
- Repeated startup/readiness, memory and representative browsing measurements, including the cost of replacing rather than stacking the current blocker.

The proposed responsibility split is sound: Zenith owns intentional access and native boundaries; uBO Lite supplies replaceable content cleanup. The experiment supports that direction but does not yet justify making the extension a trusted default for sensitive accounts.
