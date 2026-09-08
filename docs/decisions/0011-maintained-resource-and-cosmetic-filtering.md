# ADR 0011: Maintained resource and cosmetic filtering engine

Date: 2026-09-08

Status: Accepted development checkpoint

## Context

The user requested comprehensive adblocking and cosmetic filtering after the
mandatory hosts checkpoint. Growing a hostname parser into a partial EasyList
parser would duplicate complex compatibility and exception semantics. Website
code must never become the authority for site policy or filter updates.

## Decision

Use Ghostery `@ghostery/adblocker` 2.18.2, bundled reproducibly with a locked npm
dependency graph, in ClearScript V8 7.5.1.1. Native Windows x86/x64/ARM64 packages
are pinned together. The UTF-8 and URLSearchParams polyfills supply the browser
APIs used by the engine without exposing CLR objects or host I/O. Node is needed
only to rebuild the checked-in engine asset, not to build or run Zenith.

Core owns resource decision composition through `IResourceFilterEngine` and
`ResourceFilteringPolicy`: mandatory hosts first, resource rules second. App
adapts WebView2 metadata and implements the third-party engine interface. The
runtime is a separate JavaScript context inside the host process, **not** an
OS-level process sandbox. Execution budgets, soft heap monitoring, stack limits
and ArrayBuffer limits reduce resource risk but are not a native-exploit boundary.

Only fixed EasyList/EasyPrivacy subscriptions are loaded as data. Engine-native
parsing selects ordinary cosmetic hiding and exceptions; custom styles,
scriptlets and extended actions are explicitly excluded. The engine's extended
selector flag alone is insufficient to exclude custom style actions, so admission
also checks parsed filter capabilities. No downloaded resources/scripts are
installed. Network rewrite/redirect output is not executed or navigated.

`AdblockService` owns compilation, protected atomic cache replacement, periodic
updates, engine lifetime and aggregate diagnostics. It compiles replacements
off the UI path before publication. A shared lock keeps requests from using a
disposed engine. Invalid resource caches may use a bundled baseline because they
are not access-policy state; this must never relax mandatory Blacklist startup.

`ResourceRequestGuard` is installed before tabs become ready. `CosmeticFilterGuard`
registers the fixed document-created script and listens to main/nested-frame
messages. Messages carry bounded DOM hints only; the native sender URL determines
scope. CSS is serialized data applied to a constructed stylesheet. A document
nonce and URL prevent stale replies from being applied across navigation.

The detailed behavior and limits are owned by `docs/adblocking.md`.

## Evidence and regression coverage

- Engine tests cover types, parties, patterns, exceptions, query strings,
  domain-scoped cosmetics, generic hiding exceptions and excluded actions.
- Core tests verify Blacklist-first composition and normalized document context.
- Updater tests cover atomic two-list replacement, offline reopen, corrupt cache,
  partial failure, write failure, oversized/unexpected responses and cancellation.
- Opt-in live tests download and reopen the actual official subscriptions.
- Real WPF/WebView2 tests exercise intercepted fetch/image/frame requests, allowed
  resource exceptions, a Blacklist override attempt, strict CSP, dynamic class
  discovery, nested-frame scope and navigation cleanup on initial/new tabs.
- Renderer testing exposed a native crash when event-removal APIs were called
  inside a frame's Destroyed callback. Destroyed frames are now removed from
  native tracking without further calls into that destroyed native object.

## Consequences

This adds maintained native and JavaScript dependencies and their license/update
obligations. Bundled snapshots keep resource filtering usable offline. The main
costs are native runtime footprint, update compilation and bounded DOM discovery.
Full adblock-extension equivalence and universal network isolation are not claimed.

References: [Ghostery engine](https://github.com/ghostery/adblocker),
[ClearScript](https://github.com/ClearFoundry/ClearScript),
[EasyList](https://easylist.to/),
[WebView2 frames](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/frames).
