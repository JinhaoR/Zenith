# Zenith Roadmap

The roadmap describes development order, not deadlines. Each phase should end with a buildable, testable checkpoint.

## Phase 0: Foundation (Complete)

- Establish canonical terminology and product documentation.
- Create the solution structure and shared build configuration.
- Add the initial Core test project.
- Add a basic CI build workflow.

## Phase 1: Browser Shell (Complete)

- Host WebView2 in WPF.
- Add compact navigation controls, one centered top-bar Sphere search and the Sphere start surface.
- Centralize WebView2 navigation events behind an application service.
- Keep the unavailable-policy shell fail-closed while presenting denial only after an intentional request.
- Establish the fixed midnight visual system through semantic resources, including a Windows High Contrast override.
- Align the shell hierarchy and start surface with the product-experience principles.

## Phase 2: Site Policy (Complete)

Phase 2 established the Core Site Policy classifier using an immutable starter snapshot. Phase 4 now persists that seed as the initial Vault policy and replaces snapshots only through confirmed changes.

- Starter sites: GitHub, ChatGPT, OpenAI, YouTube, Wikipedia, Reddit, Microsoft Learn, Google, Stack Overflow, GitLab, MDN Web Docs and Internet Archive.
- Only HTTP(S) targets whose hostname exactly matches a starter site or is a subdomain are allowed.
- Tabs and bookmarks do not expand the starter Whitelist; every opened target still goes through the Core evaluator.
- Canonical hostname identity and URI normalization are implemented in Core, including explicit subdomain scope and deceptive-host tests.
- Focusing "Find in your Sphere" exposes a stable filterable directory of all starter sites and currently permitted bookmarks. Bookmark stars can add or remove entries directly, and saved bookmarks become sidebar shortcuts.
- Core now resolves every valid HTTP(S) hostname to Whitelist, Blacklist or the default Greylist. Blacklist takes precedence over overlapping Whitelist entries, unsupported targets fail closed, and each denied class produces a distinct native explanation.
- Native Sphere and policy-boundary surfaces unload the external document they replace; inactive tabs use WebView2 suspension and resume on activation.
- A compact current-site control shows the canonical hostname and progressively reveals the complete address for inspection or editing through the existing Sphere field and `Ctrl+L`.
- Navigation reads a validated, revisioned snapshot through a fail-closed policy-source interface that a later Vault persistence adapter can implement.

## Phase 3: Greylist and Access Grants (Complete)

The development checkpoint implements the complete password–cooldown–password flow. Core owns eligibility, persisted cooldowns, retry delays and scoped, expiring grants. The Windows adapter supplies protected credential verification and atomic state persistence. Exact policy values and restart behavior are defined in `docs/site-policy.md`; the authentication/storage design and its limits are recorded in ADR 0007.

The settings window provides saved startup sidebar and default page-zoom preferences, a filterable Sphere directory, password setup, pending temporary-access requests, and separate Vault and About pages. Phase 4 extends the original Vault placeholder into protected policy editing.

- Both challenges, restart during cooldown, failed persistence, wrong passwords, corrupt state and clock anomalies have automated coverage.
- The real WPF/WebView2 integration test exercises setup, both challenges with a fake clock, access across tabs, expiry on tab activation, and unloading active/retained documents.
- Temporary authorization does not promote destinations into Sphere discovery or bookmarks, and never overrides Blacklist.
- This is a development checkpoint, not a hardened release. Vault recovery and the broader bypass/security review remain ahead.

Greylist Access Grant State Machine

~~~text
Locked
  |
  v
FirstAuthenticationPending
  |
  v
CooldownActive
  |
  v
SecondAuthenticationPending
  |
  v
Granted
  |
  v
Expired
  |
  v
Locked
~~~

## Phase 4: Vault and Policy Changes (Complete Development Checkpoint)

- Native Settings → Vault provides current rules, review, password-authenticated staging, saved countdowns, confirmation, replacement and cancellation.
- Policy changes cover the shared password, Greylist wait, temporary visit duration, Vault wait and explicit-scope Whitelist additions.
- The user requested five-second development defaults for all three durations. Larger values, including multi-day Vault waits, use the same persisted state machine; lowering a delay must wait through the old value.
- Core owns validation, proposal IDs, revisions, clock checks and confirmation. A single protected transaction replaces policy and credentials together.
- Existing Phase 3 passwords and pending waits migrate without being reset. Corrupt initialized policy cannot fall back to starter access.
- Confirmed site additions immediately populate Sphere discovery and bookmarking. Blacklist restrictions remain authoritative.
- Automated Core, protected-store migration and real WPF/WebView2 tests cover the flow. Forgotten-password recovery and stronger tamper resistance remain hardening work; testing minima are not release defaults.

## Phase 5: Policy Hardening (In Progress)

First checkpoint: a Core-owned default-deny capability policy is wired to WebView2 permission requests, download starts and external-application launches for initial and subsequent tabs. Real renderer tests verify permission denial without profile persistence and download cancellation on both tab paths. This does not complete frame, redirect/popup or recovery hardening.

Follow-up coverage exercises allowed HTTP redirects, denied multi-hop redirects (including deceptive hostnames), script redirects, Greylist popups and unsupported blank popups. Denied navigations are cancelled and unloaded; denied popups create no unmanaged window or extra tab. Testing exposed that navigation cancellation alone does not guarantee zero network contact. That initial checkpoint did not include request-level gating or independent network verification; the next checkpoint below addresses the reproduced HTTP paths (see `docs/research-notes.md`).

The next checkpoint (ADR 0012) independently reproduced denied HTTP requests at
loopback servers and added a Core-backed request-stage document gate. The tested
redirect/script/new-tab leaks are stopped, permitted nested widgets remain
functional, and protocol failure disposes browsing controllers. Broader worker,
cache, out-of-process target, inherited-document and existing-connection coverage
remains unfinished; Phase 5 is still in progress.

- Cover redirects, popups, frames, downloads and external schemes.
- Frame compatibility choice recorded: allow embedded functionality under a Whitelisted main page without an independent Greylist challenge. A renderer regression checks cross-origin widget loading and continued denial of direct visits; comprehensive frame enforcement remains open.
- Test deceptive hosts, restarts, clock changes and corrupt state.
- Add safe recovery and last-known-good policy handling.
- Review stronger offline clock and local-snapshot rollback protection against the documented threat boundary.

## Phase 6: Permissions and Filtering

Mandatory StevenBlack synchronization and host-level resource blocking were brought forward at the user's request. The fixed variant excludes social media and cannot be overridden in the Vault. A maintained Ghostery engine now adds updateable EasyList/EasyPrivacy network rules and declarative cosmetic filtering, with bounded dynamic/nested-frame support, atomic offline-capable snapshots and Settings diagnostics (ADR 0011). This is a filtering development checkpoint, not completion of Phase 5 hardening or full extension-equivalent adblocking.

- Implement explicit website permissions.
- Add trusted Blacklist-source synchronization.
- Add updateable network-resource filtering.
- Provide diagnostics for policy and filter decisions.
- Review scriptlets/resource replacements, advanced selectors, shadow DOM and video-ad compatibility separately.
- Complete cache/worker/WebSocket and exact frame-request attribution coverage; keep mandatory Blacklist independent.

## Phase 7: Product Readiness

- Add packaging, updates and migration handling.
- Migration/versioning of Vault data.
- Complete accessibility and usability review.
- Evaluate a light palette, theme selection, persistence and system-theme synchronization.
- Perform security review and end-to-end bypass testing.
- Prepare the first public release.
