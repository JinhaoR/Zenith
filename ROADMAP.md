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

The current development checkpoint uses the general Core Site Policy classifier with an immutable development snapshot. That snapshot is seeded with a starter Whitelist so browser mechanics can be exercised against real sites while Vault-backed persistence is designed. The seed data is deliberately temporary; the classifier and policy-entry model are the foundation the future Vault will supply.

- Starter sites: GitHub, ChatGPT, OpenAI, YouTube, Wikipedia, Reddit, Microsoft Learn, Google, Stack Overflow, GitLab, MDN Web Docs and Internet Archive.
- Only HTTP(S) targets whose hostname exactly matches a starter site or is a subdomain are allowed.
- Tabs and bookmarks do not expand the starter Whitelist; every opened target still goes through the Core evaluator.
- Canonical hostname identity and URI normalization are implemented in Core, including explicit subdomain scope and deceptive-host tests.
- Focusing "Find in your Sphere" exposes a stable filterable directory of all starter sites and currently permitted bookmarks. Bookmark stars can add or remove entries directly, and saved bookmarks become sidebar shortcuts.
- Core now resolves every valid HTTP(S) hostname to Whitelist, Blacklist or the default Greylist. Blacklist takes precedence over overlapping Whitelist entries, unsupported targets fail closed, and each denied class produces a distinct native explanation.
- Native Sphere and policy-boundary surfaces unload the external document they replace; inactive tabs use WebView2 suspension and resume on activation.
- A compact current-site control shows the canonical hostname and progressively reveals the complete address for inspection or editing through the existing Sphere field and `Ctrl+L`.
- Navigation reads a validated, revisioned snapshot through a fail-closed policy-source interface that a later Vault persistence adapter can implement.

## Phase 3: Greylist and Access Grants

- Implement the first password challenge.
- Persist and enforce Greylist cooldowns.
- Implement the second password challenge.
- Issue scoped, expiring Access Grants.

Greylist Access Grant State Machine

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

## Phase 4: Vault and Policy Changes

- Build the protected Vault interface.
- Stage access-affecting edits as Policy Changes.
- Persist the multi-day waiting period.
- Require re-authentication and confirmation before atomic application.

## Phase 5: Policy Hardening

- Cover redirects, popups, frames, downloads and external schemes.
- Test deceptive hosts, restarts, clock changes and corrupt state.
- Add safe recovery and last-known-good policy handling.

## Phase 6: Permissions and Filtering

- Implement explicit website permissions.
- Add trusted Blacklist-source synchronization.
- Add updateable network-resource filtering.
- Provide diagnostics for policy and filter decisions.

## Phase 7: Product Readiness

- Add packaging, updates and migration handling.
- Migration/versioning of Vault data.
- Complete accessibility and usability review.
- Evaluate a light palette, theme selection, persistence and system-theme synchronization.
- Perform security review and end-to-end bypass testing.
- Prepare the first public release.
