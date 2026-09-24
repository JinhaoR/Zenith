# Services, trusted infrastructure and local extensions

The user chooses meaningful services. Zenith handles known web complexity without
modeling the complete dependency graph. Registry membership never grants access.

| Concept | Responsibility |
| --- | --- |
| Registry | Local curated knowledge, descriptions and reviewed evidence. |
| Service | User intent and visible entry points, such as mail.google.com. |
| Trusted infrastructure | Reviewed shared endpoints requiring explicit activation. |
| Local extension | A deliberate user exception, associated with a service for explanation. |
| Vault | Review, authentication when enabled, existing wait, explicit confirmation. |
| SitePolicy | Authoritative site/document decisions, including Blacklist precedence. |

Infrastructure and local exceptions grant profile-wide hostname access, including
direct visits. Service labels explain intent; they do not provide service-only
isolation. Services do not own domains.

## Catalog and service proposals

Core.Registry owns immutable definitions, validation, queries and pure proposal
preparation. App's JsonServiceRegistryLoader loads bundled Data/Registry JSON into
a complete validated snapshot or returns failure. Settings loads it lazily,
outside browser startup and navigation.

Catalog schema 1 remains: registry.json contains revision and explicit manifest;
capabilities.json, infrastructure.json and services/*.json contain curated data.
Limits remain 256 KiB per file, 8 MiB per catalog, depth 16 and 1,000 entries per
collection. Deploy coherent bundles; atomic publication is not a transaction over
concurrent edits to multiple files.

ServiceDefinition.EntryPoints exposes the service's own requirements; the JSON
field remains domains for compatibility. Name, category, description, status,
ecosystems and descriptive capabilities remain. Capabilities are not browser
permissions. New ServiceAccessProposalBuilder proposals use only service entry
points and never expand infrastructure associations. Missing or blocked shared
infrastructure does not prevent preparation of reviewed service entry points.

Initial reviewed proposals: arXiv (arxiv.org), GitHub (github.com), Gmail
(mail.google.com). Other entries remain read-only until reviewed. No parent,
sibling, www alias, wildcard or subdomain scope is added implicitly. Existing
manual Vault scope controls and Greylist compatibility rules remain unchanged.

Domain metadata retains purpose, required/optional state, provenance and dependency
classification. Classification alone does not make a permission eligible. Explicit
permissionApplicability remains required_navigation, optional_navigation,
background_only or unknown. Eligibility requires recorded review, source reference
and explanation. Optional service entry points are unchecked until selected.
Provenance has no confidence score; textual review metadata is not a signature.
Existing requires associations and GetDomainRequirements remain descriptive
compatibility APIs, never permission-expansion instructions.

## Infrastructure activation

InfrastructureBaselineProposal.Prepare freezes independent required_navigation
infrastructure endpoints. Optional, unknown and background-only entries are not
selected. Missing review on a required_navigation endpoint rejects the baseline.
Blacklist on any selected hostname also prevents activation.

The initial baseline is accounts.google.com. oauth2.googleapis.com remains a
background/API description and is not proposed. No Microsoft, certificate or CDN
hosts were added without review. Existing evidence is documentation review, not
exhaustive authenticated compatibility testing.

Settings > Vault > Shared infrastructure and local exceptions offers Review
infrastructure baseline. It shows all selected hosts, new/existing access,
evidence, catalog revision and frozen identity. Review neither stages nor grants
access. Start wait and Confirm and apply retain existing authentication and delays.
Activation creates ordinary exact-host Whitelist entries only where needed;
already-permitted manual scopes remain unchanged. It cannot mix service, timing,
password, removal or broader-scope edits. Confirmation rechecks revision and
Blacklist and atomically commits rules and the InfrastructureActivation receipt.

Every redirect receives its own Core decision. Trusted means reviewed for site
access, not trusted page code, OAuth clients, tenants or native privileges. TLS,
origin isolation and capability rules are unchanged. Newly infrastructure-created
permissions are omitted from generated Sphere service listings but remain
inspectable/removable in Vault. Manual permissions and explicit bookmarks are not
hidden. Created-permission IDs are presentation/history metadata, never authority.

## Local extensions

The same Vault section accepts an explicit service, exact hostname and reason.
Core requires an already-Whitelisted service entry point, captured in the frozen
LocalServiceExtensionProposal. Staging/confirmation recheck context and destination,
including mandatory Blacklist. This is a user-approved exception, not a claim of
Zenith-reviewed dependency evidence. Approval does not require global curation.

LocalServiceExtension receipts contain the frozen service/host/purpose, Vault
operation, time, applied revision and permission-instance ID. They live only in the
protected local envelope, never bundled JSON. There is no upload or promotion.
Multiple service labels can reference one existing exact permission. Broader
existing coverage is not converted or narrowed: review that scope manually.

Remove access through existing Vault hostname removal. This affects profile-wide
access, not only one service association. Historical receipts remain, but active
presentation requires the same permission ID in current Sites. Removal/recreation
cannot reattach an old exception. There is no ownership or reference-count resolver.

## Unknown authentication: advisory foundation

UnknownAuthenticationSuggestion is a pure, ephemeral explanation model. Given a
last committed hostname, a denied Greylist destination, current policy and a
catalog, it can explain an unambiguous context:

> While using Overleaf, Zenith blocked login.university.edu. This may be related
> to authentication, but Zenith has not reviewed this endpoint.

A compromised allowed page can cause the same transition to an attacker. This is
not verified discovery. The model provides no grant, proposal or persistence
operation. Ambiguous service context, known catalog endpoints, Blacklist results
and non-hostname inputs do not produce suggestions.

Only the tested foundation is included. Native event collection and the blocked
navigation suggestion UI are not wired up. Future adapters must use native last
committed/navigation/frame/opener context, not pending URLs or page messages. Do
not retain credential-bearing queries, authorization codes or POST bodies, or
replay authentication requests automatically. Existing Greylist/Access Grants,
embedded compatibility, Blacklist, TLS and capabilities are unchanged.

## Persistence, migration and updates

Protected envelope version 8 adds InfrastructureActivations and LocalServiceExtensions
beside existing ServiceApproval, PermissionInstance and PermissionAttribution
history. Only current VaultState.Sites becomes SitePolicy. Receipts, catalog entries,
IDs and fingerprints never authorize navigation or restore removed access.
New infrastructure/local additions use existing ManualVaultChange attribution for
the explicit user operation; separate receipts explain type and reason. No new
attribution graph is introduced. Historical service/infrastructure edges remain readable.

Version-7 migration validates old state, adds empty collections and performs one
atomic replacement. It preserves identities, approval contents, policy revision,
pending scopes/IDs/deadlines, settings, password choice, credentials and Greylist
waits. It never reinterprets old Gmail sign-in grants as baseline activation. Frozen
old service proposals retain their reviewed expanded scope. New service preparation
uses entry points only. Confirmation never reloads or re-expands the registry.

Version-5/6 compatibility remains, including conservative pre-7 legacy identities.
Previously authorized version-1 through version-4 password migration is unchanged.
Missing version-8 fields, invalid references and changed proposal identities fail
closed. Failed migration/confirmation writes leave prior bytes intact.

The 1 MiB protected-file limit remains. New receipt collections are each capped at
1,000; identity/attribution collections remain capped at 10,000. Oversized writes
fail before replacement; history is never silently pruned. Service proposal hash
format 1 remains unchanged. New types have separate versioned content identities;
these hashes are not signatures.

Catalog additions, removals or text edits do not change active permissions. Later
baseline activation requires fresh review and Vault confirmation. Missing catalogs
disable new preparation without affecting active policy, ordinary Vault edits or
frozen pending changes. Mandatory Blacklist remains independent and always wins.

## Deprecated direction and limitations

Complete dependency expansion, ownership assumptions and reference-counted removal
are no longer the intended design. Preserve useful identities and historical formats
without a second authorization system. ADR 0027 supersedes this direction while
retaining ADR 0025/0026 data compatibility.

No discovery, service repair/removal automation, remote updates, community catalog,
contextual service-only permission or runtime authentication observer is included.
Broader infrastructure coverage needs separate endpoint review.

## Verification

Run dotnet restore Zenith.slnx, dotnet build Zenith.slnx and dotnet test Zenith.slnx.
Set ZENITH_WEBVIEW_TESTS=1 for native browser regressions.

Tests cover independent service/baseline proposals, exact hosts, Blacklist, both
password modes, failed writes, frozen update scope, local history without authority,
advisory unknowns, version-7 expanded approval/pending migration, older compatibility,
unavailable catalogs, protected roundtrips and native WPF review/stage/confirmation.

Manual checks in an isolated profile: review Gmail (mail.google.com only), then the
baseline (accounts.google.com). Verify both await Vault confirmation. Add a local
exception for an accessible service, remove its hostname through Vault, and verify
history does not restore access. Restart during waiting and check the same frozen
proposal remains. Do not use private credentials for these checks.

### Implementation validation, 2026-09-23

Restore and build succeeded with zero warnings/errors. The final full run with
ZENITH_WEBVIEW_TESTS=1 passed 363 Core tests and 213 App tests (576 total).
Three separate opt-in scenarios were skipped: the account-boundary investigation,
upstream Blacklist download, and official adblock subscription download. The normal
native runner passed its frame/OOPIF enforcement, document clearing, file chooser,
authentication, Greylist, rendering, browser-data and extension scenarios. All four
new native Vault UI cases passed (baseline/local exception with passwords on/off).

This change adds 35 cases: 19 Core and 16 App. Existing historical attribution and
migration cases remain covered. The initial restricted restore could not contact
NuGet; restore succeeded with network access. An intermediate full run found one
old storage-version assertion; it was updated before the final passing run.

Ignored test artifacts:

- build/registry-simplification-tests/final_net10.0_20260923162559.trx
- build/registry-simplification-tests/final_net10.0_20260923162728.trx

### Files changed for this simplification

These are changes from the already-present Phase 3/4A workspace, not an inventory
of all earlier uncommitted registry work.

- Core registry: ServiceDefinition.cs, ServiceRegistry.cs,
  ServiceAccessProposalBuilder.cs, RegistryContentIdentity.cs;
  new InfrastructureBaselineProposal.cs, LocalServiceExtensionProposal.cs and
  UnknownAuthenticationSuggestion.cs (under src/Zenith.Core/Registry).
- Core Vault: VaultState.cs, VaultService.cs, VaultProposalRules.cs;
  new RegistryApproval.cs and RegistryVaultProposal.cs (under src/Zenith.Core/Vault).
- App: Access/ProtectedAccessStore.cs, MainWindow.xaml.cs,
  Settings/VaultPanel.xaml, Settings/VaultPanel.xaml.cs;
  new Settings/VaultPanel.Registry.cs and Registry/RegistryProposalPresentation.cs;
  Registry/ServicePresentation.cs (under src/Zenith.App).
- Bundled data: Data/Registry/registry.json and infrastructure.json.
- Core tests: Registry/ServiceAccessProposalTests.cs,
  Registry/PermissionAttributionTests.cs and new
  Registry/InfrastructureAndLocalExtensionTests.cs (under tests/Zenith.Core.Tests).
- App tests: Access/VaultStoreTests.cs, Registry/ServiceApprovalPersistenceTests.cs,
  Registry/PermissionIdentityPersistenceTests.cs, Registry/ServicesUiTests.cs and new
  Registry/RegistryArchitecturePersistenceTests.cs (under tests/Zenith.App.Tests).
- Documentation: ARCHITECTURE.md; docs/product-experience.md, terminology.md,
  site-policy.md, threat-model.md, service-registry.md; historical ADRs 0025/0026
  supersession notes and new docs/decisions/0027-services-infrastructure-and-local-extensions.md.
