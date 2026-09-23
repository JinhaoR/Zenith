# ADR 0026: permission identity and attribution foundations

Status: accepted, 2026-09-23. Service Registry Phase 4A only.

ADR 0027 retains these identities and history but de-emphasizes the service-removal
and attribution-graph direction. Current protected envelope version is 8.

## Context

An immutable ServiceApproval records historical user intent. The same hostname
may subsequently be removed, recreated, broadened, narrowed or shared. A hostname
or the old ServiceOriginId creation marker cannot identify all contributions to
the current permission. Future service removal needs explicit evidence without
making that evidence a second authorization system.

## Decision

Give each confirmed permission instance a random GUID and retain its immutable
scope snapshot. Preserve IDs for unchanged rules; assign new IDs on creation and
scope replacement. Review and staging remain deterministic and allocate no IDs.
Historical instances remain after removal/consolidation but never produce policy.
The identity's creation revision prevents an older approval being rebound to a
later instance of the same hostname. Migration records the current revision for
the newly assigned identity without claiming to know the old scope's creation date.

Keep separate, additive PermissionAttribution records. Each records its instance,
source, policy revision and originating Vault operation where known. Service
attribution references the immutable approval by Vault operation ID, normalized
proposed hostname and relationship index. This retains exact relationship evidence
without copying it or loading the registry. Multiple services and an independent
manual contribution can coexist. No owner or authoritative reference count exists.

Record policy, identities, attribution and service approvals in one existing
Vault confirmation transaction. Do not change authentication, waits, mandatory
Blacklist checks, hostname/scope semantics, navigation or browser capabilities.

Use protected envelope version 7. Validate old data before migrating. Label every
pre-7 current rule LegacyMigration and retain previous approval/creation history,
without inferring current service bindings from hostname coverage. Preserve all
version-5/6 authentication choices, pending proposals and cooldowns. Earlier
authorized password migrations are unchanged. Write the complete migration once,
atomically, before publishing identities. New-schema absent metadata is corruption,
not an instruction to migrate or regenerate IDs.

## Consequences

Future removal can distinguish a permission instance from a later rule on the
same host and can inspect all recorded contributions. Legacy ambiguity remains
explicit; it must not justify automatically deleting existing permission.

History has a 10,000-record bound per collection and remains subject to the
existing 1 MiB protected-file limit. A failed/oversized write leaves prior policy
and metadata intact. Automatic pruning or archival is not introduced here.

Service retirement, attribution resolution for removal, frozen removal proposals,
repair, update handling and UI remain separate work. Active permissions still
come exclusively from VaultState.Sites through the existing policy evaluator.
