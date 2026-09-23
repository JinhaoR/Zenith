# ADR 0027: services, trusted infrastructure and local extensions

Status: accepted, 2026-09-23, implementing the reviewed simplification.

## Context

Complete dependency modeling and service-based permission retirement exceed Zenith's
intentional-access requirements. Users choose services; reviewed shared infrastructure
and explicit local exceptions support ordinary workflows. Preserve Phase 3/4A data.

## Decision

Prepare new service proposals only from visible entry points. Keep infrastructure
associations descriptive. Independently activate reviewed required-navigation
infrastructure through Vault, creating ordinary exact-host rules. Local exceptions
also use Vault. Both grant profile-wide hostname access, including direct visits;
service labels explain intent rather than provide service-only isolation.

Retain authentication, active waits, revision checks, mandatory Blacklist and explicit
confirmation. There is no new navigation authority. Frozen proposals and receipts
commit with policy in protected envelope version 8. Preserve old proposal hashes,
validation, identities, attribution and pending scopes. Migration infers no new
permissions or associations. Confirmation never reloads the registry.

Unknown-authentication context is an ephemeral advisory foundation. No runtime
traffic observer, discovery or automatic proposal creation is introduced. Future
native context collection must not treat page claims as authority.

## Consequences

Infrastructure-created entries are hidden from generated service listings but remain
inspectable/removable in Vault. Existing permissions retain their meaning. Local
exceptions are local-only and removed through ordinary hostname changes; history
never restores access. Catalog updates require new proposals to change permission.

This supersedes ADR 0025's service/infrastructure expansion and de-emphasizes ADR
0026's removal motivation. Preserve useful identities and historical formats without
building an ownership or reference-count removal engine. Service repair/removal
automation, remote updates, community catalogs and contextual permissions are excluded.
