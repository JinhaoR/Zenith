# ADR 0023: descriptive registry relationships and proposal containers

Status: accepted, 2026-09-21. Phase 1.5 only.

Future service selection needs to explain relationships without turning the
catalog into authorization. Extend Core registry definitions with optional
relationship provenance and a descriptive domain dependency type. Keep source
references inert and review metadata informational; do not introduce confidence
scores or trust-based permission decisions.

Represent service-to-infrastructure edges as immutable objects so evidence can
belong to the relationship rather than to a shared infrastructure definition.
Preserve both edge and domain evidence in flattened query results. The loader
accepts existing schema-1 string references and explicit relationship objects.
Omitted classification means unknown, not navigation. Existing bundled data is
not reclassified or given invented provenance. Unsupported input remains a
whole-catalog loading failure, independent of policy startup.

Add immutable ServiceSelection, AccessProposal and ProposedDomain containers in
Core.Registry. Store one registry revision on the selection and expose it through
the proposal. Reasons belong to individual exact-host candidates. Validate shape,
copy collections and reject duplicate hosts; do not resolve dependencies or
validate authorization. These are possible changes, never approved grants.

Keep all Vault, SitePolicy, navigation evaluation, Access Grant and browser
capability implementations unchanged. A future phase must design the builder,
reviewed content identity and durable approval provenance before connecting the
containers to Vault. See service-registry.md for the data format and limitations.
