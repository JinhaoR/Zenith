# ADR 0019: Optional password protection

Date: 2026-09-19. Status: accepted by explicit user request.

## Context

The user wants cooldowns to supply the default intentional-access friction, with
password protection available through Settings. They explicitly requested turning
off password prompts for existing profiles too. Earlier mandatory-password
requirements are superseded by this decision, not by an inferred recovery route.

## Decision

Core owns the authentication requirement. Fresh application startup initializes
protected policy and cooldown storage without a password. Temporary access still
requires a request, the captured wait and explicit confirmation; grants retain
their exact-host scope, expiry and session-only lifetime. Vault edits still wait
and require explicit confirmation. Website/native trust boundaries are unchanged.

Vault settings can enable a new shared password, replace it or turn protection
off. These are staged changes. The currently active mode governs both stages;
an enabled password cannot be disabled without it. Invalid conflicting choices
are rejected. The password-enabled flag, verifier, revision and proposal
consumption are saved atomically. Disabled mode does not call a password verifier
or simulate a successful password authentication.

The version-5 DPAPI envelope requires an explicit boolean password flag. Older
supported envelopes migrate once with protection off and the active verifier and
retry delays cleared, preserving policy, settings and pending request/proposal
identities and deadlines. A pending password change remains an explicit proposal,
never auto-applies. Later launches preserve the user's selected mode. Missing or
corrupt initialized state cannot acquire a passwordless default.

## Consequences

No password setup is needed for basic browsing. Password changes remain available
under Settings → Vault. This is intentional friction, not Windows account
authentication, website login management or an OS tamper boundary. Passwordless
mode retains clock, persistence, classification and mandatory Blacklist checks.

Historical ADRs describe their original checkpoints. Current semantics are owned
by site-policy.md. Protected previous envelopes may contain old verifiers;
turning protection off is not secure erasure or forgotten-password recovery.

## Verification

Core and storage tests cover cooldown-only requests, restart, explicit
confirmation, exact-host scope, expiry, failed persistence, clock rollback,
staged enable/disable, authentication when enabled, migrations and a missing
current-schema password flag. The native GreylistOpenScenario exercises both
the passwordless dialog and optional-password Vault UI with an isolated profile.
