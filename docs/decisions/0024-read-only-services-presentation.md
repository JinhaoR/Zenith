# ADR 0024: read-only Services presentation in Settings

Status: accepted, 2026-09-21. Phase 2 only.

Use a separate Services section in the existing Settings window to expose the
curated registry for review. Keeping it apart from Your Sphere and Vault avoids
suggesting that catalog membership is approval or that selection edits policy.
Reuse native settings styles and scrolling rather than redesigning the shell.

App presentation models consume only validated immutable registry snapshots.
They group services by category, filter local names/categories, retain relationship
semantics and expose service details. No policy or persistence dependency is
provided. The WPF control adapts search and selection into presentation state.
It does not create ServiceSelection, AccessProposal or Vault edits.

Load on first opening Services, not browser startup. Expose loading/unavailable
states and technical diagnostics with explicit retry; never show unvalidated or
partial data. Evidence references remain plain text. Missing classifications and
provenance remain explicitly unknown/absent. Experimental is a catalog label.

Do not add launch actions: the registry has no deliberately modeled canonical
entry points. Never infer service approval from manually Whitelisted hostnames.
Existing navigation, Vault, Access Grant, cooldown and confirmation behavior stays
unchanged. Phase 3 requires a separate approved design for reviewed proposals,
exact scope and durable approval provenance before any integration is added.

See [service-registry.md](../service-registry.md) for behavior and verification.
