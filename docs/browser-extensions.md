# Built-in browser extensions

Initial integration: 2026-09-20. Zenith bundles the unmodified official **Edge**
uBlock Origin Lite 2026.907.2003 package already examined in the
[isolated experiment](ubolite-webview2-experiment.md). This is a pinned initial
version, not a claim that it is the latest upstream release.

## Initialization and ownership

`Zenith.App/Extensions/BundledExtensions.cs` is the hardcoded enabled-extension
configuration and WebView2 adapter. There is one approved extension and no
user-supplied path, store, install/remove command, or website-to-host bridge.
There is no extension logic in Core.

The official archive and redistribution notices live in
`src/Zenith.App/Extensions/Bundled/`. MSBuild extracts the archive into intermediate
output and copies its complete contents to `BundledExtensions/uBOLite` in build
and publish output. Distribute the complete published folder. No download or
extraction into a temporary directory happens at browser startup.

Before creating WebView2, MainWindow verifies the archive against the compiled-in
SHA-256 and checks every unpacked file against that archive. Additional executable
or resource files are rejected. Chromium-generated `_metadata/generated_indexed_rulesets/_rulesetN`
files are permitted; they are compiled DNR data, not shipped extension sources.
This check detects damaged or altered packaging. It is not an OS boundary against
same-user malware or changes made after validation. Use an installation protected
appropriately for its Windows user, and close Zenith before building or replacing
its installed files. A read-only machine-wide installation needs separate validation
because Chromium generates this metadata beside the unpacked extension.

Startup sets WPF `CreationProperties.AreBrowserExtensionsEnabled = true` before
`EnsureCoreWebView2Async`, preserving existing UDF/runtime options. New tabs reuse
that environment. All existing transport, document/frame, resource and capability
guards initialize before the initial tab becomes ready. After the existing legacy
service-worker cleanup, the loader calls `Profile.AddBrowserExtensionAsync` with
only the verified bundle directory. The same path is reinstalled each application
startup to restore registration after cleanup or an application package replacement.
It must return an enabled extension within 30 seconds. No tab can become usable
before installation succeeds; failed initialization disposes controllers, keeps
browsing unavailable, and shows a native repair/restart notice.

WebView2 support exists in SDK 1.0.4129.50 and was exercised on runtimes
153.0.4234.32 and 153.0.4234.48. Extensions are disabled by WebView2 default. An environment already
using the same UDF with a different extensions option can reject initialization;
close all Zenith instances before restarting after this change. See Microsoft's
[environment contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2environmentoptions.arebrowserextensionsenabled)
and [installation contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2profile.addbrowserextensionasync).

## Profiles, settings and updates

The [browser data model](browser-data.md) is unchanged: one persistent default
profile shared by tabs, under WebView2's default executable-adjacent UDF unless
the host supplies a different location. Extension registration and settings live
in that profile; code stays in the installed bundle directory. Clearing browsing
data does not delete bundled code, and a subsequent launch installs it again.
Do not promise that all extension settings are reset by the browsing-data API.

The unpacked manifest has no `key`; identity is derived from its absolute path.
Keep that path stable across application updates. Moving the application or
changing the profile can produce a new extension identity/settings scope. Do not
copy private Chromium profile files to migrate it. Reinstalling the same path
was tested; a real Evergreen runtime-version transition remains unverified.

The existing Content Protection status view continues to display WebView2 metadata.
No new UI or dashboard exception is added. There is no normal Edge toolbar or
extension store in this host. Ordinary navigation to `chrome-extension:` remains
denied by Core. The native metadata API has no version property; the bundle version
is recorded here and in code, not inferred from private profile storage.

uBO uses its upstream defaults, including packaged DNR rules and its normal
filtering-mode behavior. Zenith does not send internal dashboard commands, force
Complete mode, add imported lists, or enable unsupported APIs. Packaged scriptlets
and content replacement rules are executable trusted extension functionality.
Custom user-script support was unavailable in the earlier experiment; it is not
enabled through a workaround here.

No automatic extension updater is implemented. Update the reviewed archive,
compiled version/hash and notices in a Zenith release, close all controllers before
activation, and test installation, persistence and security scenarios again.
Build the replacement using fresh intermediate output and a clean publish staging
directory so removed upstream files cannot linger. Preserve the existing browser
profile; do not delete its UDF as part of replacing extension files.
The package has no store `update_url`. Keep upstream license/source notices with
distribution and supply corresponding source and dependencies for public releases.
The extension archive is about 9.6 MB; 924 unpacked files total about 35 MB before
Chromium's generated metadata. The current Ghostery filtering adapter remains
active; removing it or resolving overlapping-filter UX is a separate change.

## Security boundary and limitations

* Core remains the authority for visible document access. Native top-level,
  redirect, popup and recursive frame guards are unchanged. An extension exception
  cannot override a Core denial. uBO may add its own denials/redirects, and an
  extension interstitial may itself be rejected as an unsupported navigation.
* Existing WebView2 request/transport decisions remain in Zenith's adapters.
  **This does not establish interception of all extension background traffic.**
  An extension worker with host permissions can make its own requests; WebView2's
  per-view guards are not a general extension network sandbox. Do not claim that
  extensions cannot contact Blacklisted hosts in the background. That is distinct
  from opening a denied document, under Zenith's existing site-access model.
* uBO is a trusted content-processing dependency. Its all-URLs host access and
  scripting permission can read/change authenticated page content. A compromised
  upstream package could expose data even without the cookies API. The pinned hash
  identifies the reviewed bytes; it is not an independent publisher signature or
  a complete extension audit. Extensions gain no Vault, filesystem or native policy
  commands from this integration.
* No TLS exceptions, sandbox flags, origin-isolation changes, permission bypasses
  or arbitrary extension-scheme navigation exceptions were added. Existing website
  service-worker restrictions and F02/F03/F06 enforcement remain intact.
* Internal management means Zenith only installs its approved package. It does not
  remove unfamiliar WebView2 component extensions (such as PDF/Clipboard). It is not
  an admission boundary against Windows administrators, injected enterprise policy,
  or local code that modifies the application/profile or calls WebView2 directly.
* Installation/enabled status does not certify every rule is ready or compatible.
  This adds content filtering, not a sensitive-account security certification.

See [Microsoft's security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
and [extension APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2browserextension).

## Verification

Validation completed on 2026-09-20 with SDK 1.0.4129.50 and WebView2 runtime
153.0.4234.48 (the earlier run used 153.0.4234.32). Restore, Debug/Release builds and publish succeeded with no build
warnings/errors. The final full suite passed **284 Core tests and 102 App tests**,
including the native WebView2 scenario group; three separate opt-in investigation/
upstream-download tests were skipped. Final TRX files are in
`build/extension-verification/final/` (ignored local artifacts). Published output
was checked for the complete 924-file package, pinned version and redistribution
notices. Native GUI scenarios were automated; no manual real-account login or
controlled migration of an existing profile across runtime versions was performed.

`BundledExtensionsTests` verifies the actual shipped bytes and rejects changed
archives, changed/missing files, extra files and traversal paths. The native
`BundledExtensionScenario` opens real Zenith windows with disposable profiles and
loopback servers. It checks enabled status and stable ID across a browser-process
restart, allowed page DOM, packaged EasyList/EasyPrivacy request blocking with
disable controls, and zero destination requests for denied direct/307 navigation.
It also verifies that the extension dashboard remains inaccessible as a website.

The existing synthetic Ghostery test uses a neutral hostname with an explicit
fixture rule so uBO cannot preempt the native response being asserted. The historical
connection-coverage probes run with uBO temporarily disabled on a fresh fixture
document, then re-enable it: their positive controls must measure Zenith's own
adapter coverage, without mistaking extension blocking for a Zenith guarantee.
Other MainWindow navigation, lifecycle, rendering and capability scenarios run
with the bundled extension enabled.
The standalone file-chooser allowed/denied tests also install uBO before exercising
their top-level, nested and verified OOPIF contexts. The separate injected-invalid-
policy chooser tests retain their minimal fixture to isolate adapter failure behavior.

Run the full native suite with:

```powershell
dotnet restore Zenith.slnx
dotnet build Zenith.slnx -c Release
$env:ZENITH_WEBVIEW_TESTS = '1'
dotnet test Zenith.slnx -c Release --no-build --logger trx --results-directory build/extension-verification
```

For just the bundled-extension native scenario, also set
`ZENITH_EXTENSION_TESTS_ONLY=1` and select `WebViewNavigationTests`.
No real account credentials are used. Anonymous public-site results from the earlier
standalone experiment are historical evidence, not a new authenticated-service test.

Manual release checks: launch a complete published folder with an isolated UDF;
check the existing Content Protection status, open an allowed page, exercise a
denied link/redirect/popup, restart and check the status again. With Zenith closed,
remove a bundle file from a disposable installation and confirm that browsing
stays unavailable; restore the complete package before continuing. Test common
sites and runtime updates before widening deployment.
