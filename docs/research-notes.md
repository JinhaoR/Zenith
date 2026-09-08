# Research Notes

This file contains unresolved investigations. It is non-normative: implementation must follow the accepted policy documents and architectural decisions, not tentative notes here.

## Decision Status

Possible states:

- Open
- Investigating
- Decided
- Deferred
- Rejected

## Active Questions

### Site Identity — Decided

ADR 0002 selects normalized hostnames with explicit subdomain scope. HTTP and HTTPS, ports and paths do not alter Site Policy identity. DNS names use lowercase ASCII IDN form for comparison. User-facing display of internationalized names can be revisited as an interface concern without changing the accepted policy identity.

### Access Grants — Decided

The confirmed scope, durations and restart behavior are recorded in `site-policy.md`. ADR 0007 records the implemented authentication, persistence and session-clock design.

### Policy Changes — Development Rules Decided

ADR 0008 and `site-policy.md` define staged changes, five-second user-requested testing defaults, old-delay protection and explicit confirmation for every change. Release timing defaults/minimums and future-version migration/recovery remain open. Clock anomalies use the existing conservative session checks and documented offline limitations.

### Authentication — Initial Setup Decided; Recovery Deferred

Both Greylist challenges and the Vault use the same credential. The verifier design is recorded in ADR 0007; staged password changes are implemented under ADR 0008. Forgotten-password recovery remains unresolved and has no immediate reset route.

### Offline Time and Local State — Open Hardening Review

The Phase 3 implementation detects in-session clock anomalies and restart rollback below a saved high-water mark. Offline progress relies on UTC deadlines; a forward clock change while Zenith is closed or a complete older local snapshot cannot be reliably distinguished from legitimate progress without an additional trusted source. Determine whether stronger protection is required beyond the current local-administrator scope limit, and evaluate trusted-time and anti-rollback options before making stronger claims.

### WebView2 Enforcement

2026-09-07: The local-response renderer regression observes a redirected document request in `WebResourceRequested` even though Zenith cancels its `NavigationStarting` and unloads the page. [Microsoft documents that GET requests may occur while the host responds to navigation](https://learn.microsoft.com/mt-mt/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2navigationstartingeventargs?view=webview2-winrt-1.0.1056-prerelease). Navigation cancellation is therefore not proof of zero network contact. Add a request-level gate and verify with an isolated server/network observer before claiming otherwise. The current tests synthesize all responses and do not measure actual external contact.

Frame compatibility decision: the user accepts embedded frames as functionality of a Whitelisted top-level page without a separate Greylist challenge. See `site-policy.md` for the accepted scope. Inherited `about:blank`/`srcdoc` content, nested frames, Blacklist enforcement and frames under a temporary Greylist page still need detailed enforcement work. This is separate from ordinary image/script/resource filtering.

2026-09-08: An independent loopback HTTP harness reproduced actual denied redirect
and script-navigation requests reaching the server. ADR 0012 adds a request-stage
document gate using CDP frame IDs and Core policy; the same network assertions
now pass, including a new-tab path. Allowed redirects and nested widgets remain
positive controls. Core now explicitly checks embedded documents under temporary
pages without broadening grants. General inherited-document, worker/cache,
out-of-process target and connection-lifetime coverage remains open; the new
results establish only the tested HTTP paths, not universal network isolation.

- Which events cover redirects, popups, downloads, frames and external schemes?
- Which requests can be cancelled before content becomes visible?
- How should service workers, cached resources and browser profile data be handled?

### External Lists

- Which site and resource-list formats will be supported?
- How will authenticity, integrity and version rollback be handled?
- How should conflicting local and external classifications be represented?

## Note Template

```markdown
## YYYY-MM-DD — Question

### Goal
What needs to be learned?

### Evidence
Sources, experiments and observations.

### Preliminary conclusion
Current understanding, including uncertainty.

### Next action
What would resolve the question?

### Experiments
Small prototypes or tests used to answer the question.
```

When research produces a decision, update the owning specification and create an ADR when the choice has meaningful alternatives or long-term consequences.
