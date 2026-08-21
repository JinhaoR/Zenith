# Zenith Roadmap

The roadmap describes development order, not deadlines. Each phase should end with a buildable, testable checkpoint.

## Phase 0: Foundation

- Establish canonical terminology and product documentation.
- Create the solution structure and shared build configuration.
- Add the initial Core test project.

## Phase 1: Browser Shell

- Host WebView2 in WPF.
- Add basic navigation controls and the Zenith start page.
- Centralize WebView2 navigation events behind an application service.

## Phase 2: Site Policy

- Implement URI normalization and site identity.
- Implement Whitelist, Blacklist and Greylist classification.
- Deny unclassified navigation by default.
- Add policy-decision explanations and Core tests.

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
- Perform security review and end-to-end bypass testing.
- Prepare the first public release.

