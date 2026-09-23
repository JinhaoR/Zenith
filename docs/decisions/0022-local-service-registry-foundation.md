# ADR 0022: local descriptive Service Registry

Status: accepted, 2026-09-20. Phase 1 only.

Users recognize services rather than infrastructure hostnames. Zenith currently
stores hostname policy, while service-like names in the starter catalog and Vault
are presentation metadata. Introduce curated service knowledge without changing
the working authorization system.

Store versioned JSON in `Data/Registry` with an explicit manifest and one file per
service. JSON is readable, reviewable in Git and adequate for this small catalog.
There is no demonstrated need for SQLite, another project, remote updates or
automatic discovery. Copy the catalog with the application at build/publish time.

Core owns immutable descriptive models, validation and queries. App owns file
access and JSON deserialization. Publish only a complete validated snapshot, or
return a failure result. Keep the loader outside startup, Vault, navigation and
capability evaluation until a separately approved consumer needs it.

Registry capabilities describe service functions; they do not reuse native
permission enums or produce grants. Dependencies are explicit service-to-
infrastructure links. Exact normalized hosts can have multiple associations.
Initial service coverage is incomplete, and YouTube is marked experimental.

Existing Whitelist/Greylist/Blacklist decisions, subdomain scopes, temporary
grants, Vault persistence and protected editing remain unchanged. Future service
authorization must preserve reviewed dependency scope and provenance independently
of hostname consolidation. Catalog updates cannot silently expand access.

If queries later warrant SQLite, replace storage behind the descriptive API
without making that database an authorization authority. No storage migration or
policy migration is implemented by this decision.

See [service-registry.md](../service-registry.md) for the schema, failure behavior,
curation assumptions, tests and future migration constraints.
