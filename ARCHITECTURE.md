# Zenith Architecture

> **Status:** Phase 1 browser shell and Phase 2 Site Policy complete; Phase 3 Greylist access is next.
>
> The project boundaries, Sphere-first WPF shell, WebView2 host and navigation coordinator are in place. Core classifies normalized hostnames from a revisioned policy source as Whitelist, Blacklist or default Greylist, while App presents distinct native boundaries for unavailable destinations. Native surfaces unload replaced web content, inactive tabs are suspended where WebView2 permits it, and the sidebar exposes inspectable current-site identity with progressive address editing. The current source remains seeded with a temporary starter Whitelist until Vault-backed persistence exists.

## 1. Scope

This document describes:

- Solution and project boundaries.
- Dependency direction.
- Major components and their responsibilities.
- Ownership of durable and temporary state.
- The principal navigation, Access Grant and Policy Change flows.
- Failure and verification boundaries.

It does not redefine product behavior. Product meaning belongs in **docs/product-principles.md**; exact behavior belongs in the relevant policy documents.

## 2. Architectural Goals

Zenith's architecture must:

- Evaluate policy before permitting web navigation.
- Keep policy logic independent of WPF and WebView2.
- Route every navigation mechanism through one decision system.
- Make cooldowns and Policy Changes deterministic and testable.
- Keep the Vault authoritative for durable policy.
- Treat WebView2 and rendered content as untrusted.
- Fail closed when policy cannot be evaluated safely.
- Remain small enough for a focused desktop application.

Zenith does not implement a browser engine or operating-system-wide access control.

## 3. Solution Structure

~~~text
Zenith/
├── Zenith.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── Zenith.App/
│   └── Zenith.Core/
└── tests/
    ├── Zenith.App.Tests/
    └── Zenith.Core.Tests/
~~~

### Zenith.App

The Windows application and composition root.

It owns:

- WPF Views and ViewModels.
- Native Sphere start surface, navigation-boundary and recovery surfaces, Greylist gate and Vault interfaces.
- WebView2 creation, configuration and event handling.
- Translation between WebView2 events and Core requests.
- Implementations of Core interfaces for persistence, authentication, time, external lists and other platform services.
- Application startup and dependency construction.

It must not decide site classification, Access Grant validity or Policy Change state.

`Sphere` is presentation vocabulary for the ordinary environment backed by current Whitelist membership. It belongs in App copy and view naming where useful; Core models, policy evaluation and persistence continue to use Whitelist.

### Zenith.Core

The platform-independent domain and policy layer.

It owns:

- Site identity and URI normalization rules.
- Whitelist, Blacklist and Greylist evaluation.
- Navigation decisions.
- Greylist cooldown and Access Grant state machines.
- Vault and Policy Change rules.
- Permission decisions.
- Domain models and failure reasons.
- Interfaces required from storage, authentication, time and external data.

It must not reference WPF, WebView2, the filesystem, network clients or Windows UI APIs.

### Zenith.Core.Tests

Tests Core behavior without launching WPF or WebView2.

It owns tests for:

- Site identity and classification.
- Navigation decisions.
- Access Grant state transitions.
- Policy Change state transitions.
- Time and restart boundary behavior.
- Invalid, conflicting and unavailable policy.

Additional integration-test projects should be added only when there is concrete behavior that cannot be tested through Core.

### Zenith.App.Tests

Tests application-layer coordination that depends on the App-to-Core boundary but does not require launching the graphical interface.

It currently verifies that direct-address, WebView and new-window navigation requests all reach the same Core policy evaluator.

## 4. Dependency Direction

~~~mermaid
flowchart TD
    App["Zenith.App — WPF and WebView2"] --> Core["Zenith.Core — domain and policy"]
    CoreTests["Zenith.Core.Tests"] --> Core
    AppTests["Zenith.App.Tests"] --> App
    AppTests --> Core
    App --> Adapters["App infrastructure adapters"]
    Adapters -. "implement" .-> Ports["Interfaces owned by Core"]
    Core --> Ports
~~~

The dependency direction is inward:

- **Zenith.App** depends on **Zenith.Core**.
- **Zenith.Core.Tests** depends on **Zenith.Core**.
- **Zenith.App.Tests** depends on **Zenith.App** and **Zenith.Core**.
- Core defines the interfaces it requires.
- App supplies platform-specific implementations at startup.
- Core never depends on App.

Infrastructure remains inside **Zenith.App** initially. A separate infrastructure project requires an ADR and a demonstrated need, such as substantial adapter complexity or independent reuse.

## 5. Major Components

The following names describe responsibilities; they do not require one class per row.

| Component | Layer | Responsibility |
| --- | --- | --- |
| WebView2 adapter | App | Intercept browser events, create Core requests and enact returned decisions. |
| Navigation coordinator | App | Ensure direct-address, link, redirect, popup and external navigation use the same path. |
| Site identity and URI normalizer | Core | Canonicalize HTTP(S) targets and produce the hostname identity used by policy. |
| Policy evaluator | Core | Read the active revisioned snapshot, resolve Whitelist, Blacklist or Greylist and return a fail-closed navigation decision. |
| Site Policy source | Core interface, App adapter | Expose one validated active snapshot without coupling classification to its eventual persistence format. |
| Access Grant service | Core | Manage the password–cooldown–password state machine and validate Access Grants. |
| Vault service | Core | Expose the active policy and create, confirm, cancel or reject Policy Changes. |
| Permission evaluator | Core | Decide website capabilities independently from navigation access. |
| Policy store | App adapter | Persist active Vault policy and revisions atomically. |
| Temporal-state store | App adapter | Persist cooldowns, pending Policy Changes and any persistent Access Grant state. |
| Authentication adapter | App adapter | Verify credentials without exposing secrets to Core or web content. |
| Time provider | App adapter | Supply testable time information to Core. |
| External-list updater | App adapter | Fetch and validate Blacklist sources and resource filter lists. |

## 6. State Ownership

### Active Vault policy

The active Vault policy is durable, versioned state. It contains the current classifications and access-affecting configuration.

Only the Vault service may replace it, and only through a confirmed Policy Change. UI and persistence adapters must not interpret or mutate policy independently.

### Pending Policy Changes

A pending Policy Change contains:

- The exact proposed change.
- The active policy revision on which it was based.
- Its creation and confirmation-eligibility times.
- Its current state.

The existing Vault policy remains authoritative until the proposal is confirmed and applied atomically.

### Greylist cooldowns

An active cooldown is domain state rather than a UI timer. Closing a dialog or restarting Zenith must not erase or shorten it.

### Access Grants

An Access Grant is scoped, expiring state separate from durable classification. Whether it survives restart remains a site-policy decision; the architecture must support an explicit choice rather than accidental persistence.

### External lists

External Blacklist data and ad-blocking filter data are stored separately. Each source retains its identity, version, update status and last-known-good content.

Authentication secrets are never stored as ordinary policy data.

## 7. Principal Flows

### Navigation

1. **Zenith.App** intercepts a navigation attempt before external content is permitted to load.
2. The request is converted into a Core navigation request.
3. Core derives the canonical site identity.
4. The policy evaluator reads the active policy and any relevant Access Grant.
5. Core returns an explicit decision and reason.
6. App either permits navigation or cancels it and presents a native Zenith surface.

The App must not reinterpret the decision. A denied or failed evaluation cannot fall back to unrestricted WebView2 navigation.

### Greylist Access Grant

1. A Greylist decision causes App to present the native Greylist gate.
2. Credential input is sent to the authentication adapter, never to web content.
3. Core records the first successful challenge and begins the cooldown.
4. The cooldown is persisted and evaluated through the Core time abstraction.
5. After eligibility, App presents the second challenge.
6. Core issues the scoped Access Grant after successful verification.
7. The original navigation is submitted through the normal policy evaluator again.

An Access Grant never bypasses the evaluator and never modifies durable classification.

### Vault Policy Change

1. The native Vault UI submits an authenticated proposal to Core.
2. Core creates an immutable pending Policy Change tied to the active policy revision.
3. App persists the proposal while the existing policy remains active.
4. After the long waiting period, the user re-authenticates and confirms the exact proposal.
5. Core verifies its eligibility and revision.
6. The policy store applies the new revision atomically.

Views and ViewModels never write Vault policy directly.

### External-list update

1. An App background service fetches data from a configured source.
2. The adapter validates its format and metadata.
3. Valid data replaces the previous version atomically.
4. Invalid or unavailable data leaves the last-known-good version active.
5. Core consumes the validated data through an interface; it performs no network access.

## 8. Startup and Failure Behavior

Startup order is:

1. Load and validate active policy and temporal state.
2. Recover a valid previous revision where the documented recovery policy permits it.
3. Construct Core services and App adapters.
4. Initialize the WebView2 host behind the navigation gate.
5. Show the local Sphere start surface.

If no trustworthy policy can be established, Zenith enters a restricted recovery state with no external navigation. It must never start as an unrestricted browser.

State writes must be atomic. Policy revisions, pending Policy Changes and cooldown records must be validated when loaded. Time anomalies must be handled conservatively and must not shorten a required delay.

## 9. Testing Boundaries

### Core unit tests

Use fake clocks, stores and authentication results to exercise policy and state transitions deterministically.

### Adapter contract tests

Verify persistence, time and external-list adapters against the contracts defined by Core.

### App integration tests

Verify that each WebView2 navigation mechanism reaches the same coordinator and that App follows Core decisions exactly.

### End-to-end tests

Add focused end-to-end tests for security-sensitive paths after the browser shell and Vault exist.

## 10. Evolution Rules

- Prefer extending the two-project structure over adding projects prematurely.
- Add a project only when it creates a real dependency or deployment boundary.
- Keep unresolved design questions in **docs/research-notes.md**.
- Record consequential technical choices in **docs/decisions/**.
- Update this document when implemented dependencies, components or state ownership change.

Current unresolved architectural inputs include Access Grant lifetime, authentication mechanism, persistence format and precise time-tamper handling. These must be settled before their corresponding implementation is considered complete.
