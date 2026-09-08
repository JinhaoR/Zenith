# Zenith filtering bundle

`entry.js` adapts the maintained Ghostery engine to a host-side ClearScript V8
runtime. The browser never downloads or runs the engine from a CDN. No CLR host
objects are exposed. Lists are data; scriptlets, extended actions, CSP changes,
HTML filtering and resource replacements are not installed.

## Rebuild

Use Node 24 LTS with npm from this directory:

```powershell
npm ci --ignore-scripts --no-audit --no-fund
npm run build
node licenses.mjs
```

After updating ClearScript, also run `node licenses.mjs <NuGet-packages-directory>`
to refresh its runtime notices from the restored native package.

The checked-in lock file pins transitive archives and integrity hashes. The
checked-in, unminified `Filtering/Assets/engine.js` is a generated build artifact;
change the entry points or dependencies and rebuild, rather than editing it.
Node is a developer dependency only; .NET builds and end-user installations do
not need Node or npm. `engine.js` and its notices must be updated together.

Runtime packages are pinned in `Directory.Packages.props`, including native V8
for Windows x86/x64/ARM64. Keep the runtime and native versions synchronized.
Review upstream changes and run all filtering and WebView2 tests on updates.

`Filtering/Assets/cosmetics.js` is separately maintained Zenith source. It only
collects bounded DOM hints and applies returned CSS, never filter-provided JS.

## Lists and licenses

EasyList and EasyPrivacy snapshots remain separate unmodified source files with
upstream headers. They are attributed to The EasyList authors under CC BY-SA 3.0
or later; see the shipped notices and https://easylist.to/pages/licence.html.
Runtime updates use the fixed HTTPS URLs and validation in `AdblockService`.
The snapshots allow resource filtering before its first online synchronization;
they do **not** replace the separate mandatory Blacklist startup requirement.

The generated notices include MPL-2.0 Ghostery sources and dependency licenses.
ClearScript/native V8 notices are shipped separately. This does not change the
license of Zenith's own code. Before distributing modified third-party sources,
follow their source-availability and attribution obligations.
