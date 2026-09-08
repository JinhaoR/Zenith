# ADR 0004: Unload Pages Replaced by Native Surfaces

- **Status:** Accepted
- **Date:** 2026-09-02

## Context

Collapsing a WebView2 control removes it from view but does not itself define whether the loaded page may continue running. Zenith's Sphere and policy boundaries are native surfaces and must not leave the page they replaced active invisibly.

WebView2 provides sleeping-tab suspension, but that operation is best-effort. It is useful for ordinary inactive tabs but is not a sufficient guarantee when Zenith intentionally replaces a page with a native surface.

## Decision

- When the user returns a tab to the Sphere, or a policy boundary replaces its page, Zenith clears the external document by navigating that WebView to an internal `about:blank` document.
- The internal navigation is accepted only while that specific tab has a pending host-initiated clear operation, and its navigation identifier is tracked through completion.
- A new user-requested destination cancels a scheduled clear that has not started. If the clear is already in flight, the destination is held briefly and started as soon as that clear completes. This prevents the internal clear and destination navigation from racing.
- Unmatched WebView `about:blank` navigation requests are canceled without presenting a site-policy boundary.
- History notifications that still describe the initial or cleared `about:blank` document update navigation controls only. They must not replace the selected external destination or produce a policy boundary. Observed external addresses still use Core evaluation; an explicitly entered unsupported address still receives the normal denial.
- External navigation identifiers are tracked separately. Completions from superseded navigations are ignored so an aborted clear or page load cannot be presented as a failure of the current destination.
- Every external navigation before or after the clear continues through the normal Core policy evaluator.
- Ordinary inactive tabs are collapsed and passed to `TrySuspendAsync`; reactivating a tab resumes it before display.
- If an inactive-tab suspension is unsuccessful, the tab remains an ordinary retained browser tab. Native replacement still unloads the external document rather than relying on suspension.

## Consequences

Native Zenith surfaces do not conceal a still-loaded external page. Returning to the Sphere intentionally resets that tab instead of preserving a hidden page behind the start surface. Inactive tabs retain normal browser behavior while reducing background activity where WebView2 supports suspension.
