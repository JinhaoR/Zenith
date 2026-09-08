# ADR 0006: Prepare Explicit Access Grant Evaluation

- **Status:** Accepted initial boundary; activated by ADR 0007
- **Date:** 2026-09-06

## Context

Phase 3 introduces temporary authorization for Greylisted sites. Grant duration, restart behavior and authentication choices are unresolved. Settings work can proceed independently, but ordinary preferences must not become a means to issue access or edit protected policy.

## Decision

- Represent a grant in Core with an explicit normalized hostname, start and expiry.
- Inject a grant source and a time provider into the existing evaluator. The source is optional; omitting it preserves the current denial of Greylist navigation.
- Check classification first. A grant cannot override Blacklist or unsupported-target decisions, change classification, or cover unrelated hosts or subdomains implicitly.
- Keep issuance and the production grant source unimplemented until authentication, cooldown persistence, restart and timing rules are settled. A wall-clock validity check alone is not a complete cooldown or rollback defense.
- Keep non-policy settings in App, stored separately and atomically. The settings window preserves the existing browser document in its owner.

## Consequences

Future issuance has a small, testable input into the existing navigation gate. No temporary access is activated by this checkpoint. Password setup, credential protection, both challenges, persisted cooldowns, conservative clock handling and expiry of already displayed pages remain required before enabling it.

## Follow-up

ADR 0007 records the subsequent confirmed product choices and activation design. The application now supplies the Core grant service, protected authentication/cooldown adapter and native challenge flow. Allowed decisions expose the resolved Access Class so temporary authorization cannot accidentally populate Whitelist-backed Sphere discovery.
