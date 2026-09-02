# ADR 0003: Read Navigation Policy Through a Snapshot Source

- **Status:** Accepted
- **Date:** 2026-09-02

## Context

The first Phase 2 classifier received one immutable development snapshot directly. A future confirmed Vault Policy Change must be able to replace the active policy without moving persistence or classification logic into the UI. Missing, corrupt or unavailable policy must fail closed.

## Decision

The navigation evaluator depends on the Core-owned `ISitePolicySource` interface and requests the active `SitePolicySnapshot` for every navigation decision.

- A snapshot is immutable after construction and carries a non-negative revision.
- Snapshot construction validates its entries before the snapshot can become active.
- A source reports explicitly whether a trustworthy active snapshot is available.
- An unavailable or failing source produces `NavigationDenialReason.PolicyUnavailable`.
- The development build uses `FixedSitePolicySource`; a future App persistence adapter may implement the same interface and atomically expose a newly confirmed snapshot.

## Consequences

Navigation immediately observes an atomically replaced policy snapshot. Core remains independent of storage technology, while persistence failures cannot create an unrestricted fallback. Vault persistence, migration and atomic file replacement remain later work.
