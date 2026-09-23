# ADR 0025: reviewed service proposals through the existing Vault

Status: accepted, 2026-09-21. Phase 3 only.

Service/infrastructure expansion is superseded by ADR 0027. Historical frozen
proposals and approval validation remain supported as described below.

Keep the registry descriptive. Navigation applicability is independent of dependency
classification. Eligibility requires recorded evidence/review on domains and on
selecting services' infrastructure edges. Unknown/background dependencies cannot
silently become hostname permissions.

A pure Core builder consumes selection, registry and effective policy snapshots.
It freezes exact consequences, existing coverage, reasons, options and conflicts.
Canonical SHA-256 identities provide determinism/audit correlation, not authority.
The existing Vault GUID identifies each staging attempt. Confirmation never reads
the registry again.

Adapt frozen results to ordinary Vault edits with provenance. Preserve optional
authentication, waits, revision checks, Blacklist precedence and atomic persistence.
Attribution-only approval follows the full workflow. Do not mix service proposals
with unrelated settings, removal or scope edits.

Store complete ServiceApproval records with policy in envelope version 6. Newly
created entries receive creation provenance, distinct from service associations;
many approvals can reference one host. Manual scope changes clear creation tags.
Records never authorize navigation or restore missing policy. Current-schema
missing attribution is invalid; version-5 migration preserves existing choices.

Start with arXiv, GitHub and Gmail using recorded official-source curation. This is
not a complete authenticated compatibility claim. The UI prepares and transfers
reviews; selection neither stages changes nor launches a service.

Defer removal, repair, discovery and catalog distribution. Future removal needs
explicit association retirement and preservation of manual/shared access.
See [service-registry.md](../service-registry.md) for fields, sources and limitations.
