# ADR 0013: Explicit Service Scope and Batch Vault Changes

- **Status:** Accepted
- **Date:** 2026-09-08

## Decision

The user wants individual services such as Scholar, Drive and Gmail without implicitly allowing their parent or siblings. Retain Core's hostname-boundary matcher and default new Vault additions to exact-host scope. Including descendants is an explicit choice, not discovery of related services. Existing saved scopes remain unchanged.

Replace the single-site editor with a name-first batch editor. Core normalizes removals before additions, freezes the reviewed selections, and applies the whole result through the existing authenticated, delayed, revision-checked transaction. This permits a broad parent to be replaced with selected children without an intermediate policy state. Invalid selections or failed persistence cannot partially apply the batch. Blacklist precedence remains unchanged.

Friendly names are optional presentation metadata; addresses and explicit scope remain the authority and are available in expandable review details. Catalog launch addresses must match their entry; a service-only entry must not inherit a catalog's broader `www.` launch address.

Protected envelope version 4 records batch fields and requires explicit scope for each addition. Versions 2 and 3 retain their existing policy and legacy pending edits during validated migration. Version 1 retains its established development migration. Missing current-schema policy fields fail closed. Legacy single-site proposals remain readable and confirmable with their original deadline and scope.

This supersedes ADR 0008's single-site proposal limit and broad-by-default interface. It introduces no automatic cross-domain trust or login exception. Detailed behavior belongs in `docs/site-policy.md`.

## Verification

Core regressions cover parent/sibling isolation, optional descendants, batch replacement and narrowing, cancellation, caller mutation, failed writes and Blacklist changes. Isolated DPAPI tests cover migration, batch restart/confirmation and missing scope fields. The native Vault/WebView2 regression replaces broad Google access with selected services and checks allowed pages and denied parent/sibling navigation.
