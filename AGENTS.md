# Zenith Agent Instructions

## 1. Repository Purpose

Zenith is a Windows browser built with WPF and WebView2.

It is a policy-driven browser with a default-deny access model. This summary provides context only, do not infer detailed product behavior from it.

Before changing user-facing behavior, read `docs/product-principles.md`, `docs/product-experience.md`, `docs/terminology.md` and the relevant policy document.

---

## 2. Sources of Truth

Use the following documents for their designated purposes:

* `docs/product-principles.md`

  * Product identity and guiding principles.
* `docs/product-experience.md`

  * User experience, interaction hierarchy and interface character.
* `docs/terminology.md`

  * Canonical policy vocabulary and the boundary between Sphere interface language and Whitelist policy language.
* `docs/site-policy.md`

  * Site classification, navigation decisions, Greylist access and Vault policy changes.
* `docs/permission-model.md`

  * Website permissions and browser capabilities.
* `docs/threat-model.md`

  * Trust boundaries, bypass risks and failure behavior.
* `docs/adblocking.md`

  * Network filtering and external block-list behavior.
* `ARCHITECTURE.md`

  * Current system structure and component responsibilities.
* `docs/decisions/`

  * Reasons for significant architectural decisions.
* `ROADMAP.md`

  * Planned milestones and future work.

Do not duplicate detailed specifications in `AGENTS.md`.

If documents conflict, do not silently choose an interpretation. Identify the conflict and ask for clarification before changing behavior.

---

## 3. Repository Structure

### `Zenith.App`

Contains:

* WPF Views and ViewModels.
* User interaction.
* WebView2 integration.
* Presentation of decisions made by Core.

It must not contain policy or domain logic.

### `Zenith.Core`

Contains:

* Domain models.
* Site and navigation policy.
* Vault domain logic.
* Cooldown and access-state logic.
* Interfaces for time, persistence and authentication.

It must remain independent of WPF and WebView2.

### `tests/`

Contains automated tests for Core behavior and relevant integration behavior.

Core policy must be testable without launching the graphical application.

---

## 4. Architecture Rules

* Keep domain and policy logic independent of the UI.
* UI components may request and display decisions but must not make them.
* Treat WebView2 as a rendering engine, not as the policy authority.
* Route every navigation entry point through the same Core policy system.
* Centralize URI parsing, normalization and comparison.
* Use an injectable clock or time provider for cooldowns and delayed state transitions.
* Keep persisted data separate from the logic that interprets it.
* Do not duplicate policy logic across event handlers or components.
* Identify the responsible layer before implementing a new feature.
* Record significant architectural changes in `docs/decisions/`.

---

## 5. Working Process

Before modifying code:

1. Inspect the relevant code, tests and documentation.
2. Determine which layer owns the behavior.
3. Identify the applicable specification.
4. Define the expected result and relevant failure cases.
5. Ask for clarification if product behavior is unspecified.
6. Make the smallest coherent change.
7. Add or update tests.
8. Run the relevant verification commands.
9. Review the final diff for regressions and unrelated changes.
10. Update documentation when behavior or architecture changed.

Use a written plan before making broad, ambiguous or security-sensitive changes.

Do not make unrelated refactors as part of a focused task.

---

## 6. Product and Security Boundaries

* Do not change product semantics unless the task explicitly requests it.
* Do not add shortcuts around access, authentication, cooldown or Vault behavior.
* Do not introduce unrestricted navigation as fallback or recovery behavior.
* Treat access-control and Vault changes as security-sensitive.
* Do not allow website content to modify policy state directly.
* Never store or log plaintext passwords.
* Never commit credentials, secrets or private configuration.
* Do not invent authentication or cryptographic schemes without documenting and reviewing the design.
* Follow the failure behavior defined in the relevant policy and threat-model documents.

When uncertain, preserve the existing restriction and request clarification.

---

## 7. Testing Requirements

Changes to Core behavior must include relevant automated tests.

Tests should:

* Verify externally observable behavior.
* Cover relevant failure and boundary cases.
* Avoid depending on wall-clock time.
* Avoid depending on the graphical interface.
* Demonstrate that the implementation matches the applicable specification.

Security-sensitive changes require tests for likely bypass paths, not only the expected successful path.

UI-only changes may omit automated tests when they do not affect application behavior, but the solution must still build.

---

## 8. Coding Rules

* Follow established C# and .NET conventions.
* Respect nullable-reference analysis.
* Keep classes focused on one responsibility.
* Prefer explicit dependencies over global or static state.
* Avoid hidden side effects.
* Prefer clear domain types over collections of loosely related booleans.
* Add comments only for non-obvious reasoning or constraints.
* Avoid premature abstractions.
* Add dependencies only when they solve a concrete need.
* Do not modify generated files manually.
* Preserve unrelated user changes already present in the repository.

---

## 9. Build and Verification

Run commands from the repository root:

```powershell
dotnet restore Zenith.slnx
dotnet build Zenith.slnx
dotnet test Zenith.slnx
```

Keep these commands synchronized with the actual solution structure.

---

## 10. Documentation Rules

Update the document that owns the changed information:

* Product meaning → `docs/product-principles.md`
* Product experience → `docs/product-experience.md`
* Terminology → `docs/terminology.md`
* Site behavior → `docs/site-policy.md`
* Website capabilities → `docs/permission-model.md`
* Security assumptions → `docs/threat-model.md`
* Filtering behavior → `docs/adblocking.md`
* Current structure → `ARCHITECTURE.md`
* Future work → `ROADMAP.md`
* Significant design choice → `docs/decisions/`
* Unresolved investigation → `docs/research-notes.md`

Do not spread the same specification across several documents.

---

## 11. Definition of Done

A task is complete when:

* The requested behavior is implemented.
* The change follows the documented architecture and policy.
* Relevant tests have been added or updated.
* The solution builds.
* Relevant tests pass.
* Failure and bypass cases have been considered.
* Documentation matches the resulting behavior.
* The final diff contains no unrelated changes.
