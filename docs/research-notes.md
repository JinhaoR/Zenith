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

### Site Identity

- Should classification use exact hosts or registrable domains?
- How should `www` aliases, subdomains, ports and paths behave?
- Should HTTP and HTTPS share classification?
- How should internationalized domain names be displayed and compared?

### Access Grants

- Is an Access Grant valid for one navigation, one tab, a fixed duration or a session?
- Does it survive application restart?
- Does it cover only one host or an explicitly selected domain scope?

### Policy Changes

- What is the default Vault waiting period?
- May strictly restriction-increasing changes take effect immediately?
- How are pending changes handled after an application update or clock change?

### Authentication

- Are the two Greylist password challenges identical or independent?
- How are credentials stored and verified?
- What recovery process exists without creating an immediate bypass?

### WebView2 Enforcement

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
