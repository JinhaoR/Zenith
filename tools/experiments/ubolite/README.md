# Isolated uBO Lite / WebView2 experiment

This is a console-driven WPF experiment, not a production integration or a member
of Zenith.slnx. It references existing App/Core assemblies and, for two modes,
attaches their unchanged guards. The friend assembly name permits that reuse
without editing production. It must not be shipped as Zenith.App.Tests.

See [the assessment](../../../docs/ubolite-webview2-experiment.md) and
[recorded evidence](../../../docs/security-evidence/ubolite/).

## Inputs

Download official release ZIPs from
https://github.com/uBlockOrigin/uBOL-home/releases/tag/2026.907.2003 and unpack them
under build/ubolite-experiment/chromium and build/ubolite-experiment/edge. Keep the
complete folder layout, including _locales. Archive hashes are in the assessment.
Do not point the harness at a real browser profile.

~~~powershell
dotnet restore tools/experiments/ubolite/UbolExperiment.csproj
dotnet build tools/experiments/ubolite/UbolExperiment.csproj --no-restore
dotnet tools/experiments/ubolite/bin/Debug/net10.0-windows/Zenith.App.Tests.dll build/ubolite-experiment/edge build/ubolite-experiment/profile-fresh build/ubolite-experiment/result.json complete sites
~~~

Arguments: extension folder or none; isolated profile folder; output JSON; mode;
optional sites or youtube; optional fixed-runtime folder.

Modes:

- complete/basic/optimal: configure the corresponding upstream mode, enable its
  packaged test rules, and test an imported local list and custom scriptlet.
- restart: reuse an existing profile without adding the extension again.
- reinstall: call AddBrowserExtensionAsync even if the extension is present.
- extensions-off: create the environment with extensions disabled and try installation.
- zenith: attach existing network, document, resource and capability guards.
- combined: also attach the current Ghostery/ClearScript blocker and cosmetic bridge.
- inspect: inspect the extension-management page, popup and manual custom CSS.
- measure: fresh stock extension with no dashboard/import configuration; idle 35 s.
- measure-baseline: same idle interval with no extension (first argument none).

The sites option makes unauthenticated requests to Wikipedia, GitHub, Gmail,
YouTube, Reddit, Speedtest and CNN. youtube additionally samples after ten seconds.
No account login, file selection, speed test, or public posting is performed.

Results are observations, not universal pass/fail assertions: exit code zero means
the harness completed; inspect JSON errors and negative controls. Each record is
saved immediately. Timing includes fixture settling where indicated; navigation
timings are separate. Process memory sums cover WebView2 subprocesses, not the WPF
host, and working sets may double-count shared pages.

The harness invokes upstream internal dashboard messages for reproducible setup.
These are experiment conveniences, not a promised production API. It simulates
imported-list expiry by changing only that isolated extension's timestamp. The
current source uses literal loopback under Zenith guards because Core intentionally
does not exempt HTTP localhost hostnames.

Package-upgrade test: first unpack 2026.901.1442 into a stable experiment path,
run with a fresh profile, and wait for browser exit. Back up that folder, put
2026.907.2003 at the same path, then run reinstall followed by restart. This tested
procedure is not an atomic, authenticated production updater. Never replace files
in a running extension directory.

Profiles and downloaded extension code stay in ignored build/. Evidence contains
only synthetic state and public-page measurements. Existing production changes
from other tasks are left untouched.
