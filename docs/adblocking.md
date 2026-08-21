# Ad Blocking

## 1. Role

Ad blocking supports Zenith but does not define its access model.

Site classification determines whether navigation is allowed. Ad blocking determines which network resources or page elements may be loaded within an allowed page. These systems must remain separate.

## 2. List Types

Zenith may consume two distinct kinds of external data:

- **Site lists**, which can contribute entries to the Blacklist.
- **Resource filter lists**, which block advertising, tracking or unwanted subresources.

A resource filter rule must not silently reclassify a site, and a failed filter update must not weaken site-access policy.

## 3. Update Model

External lists should be updated periodically through a replaceable provider layer.

Updates must:

- Use identified and reviewable sources.
- Be downloaded as data, never executed as code.
- Be validated before activation.
- Be applied atomically.
- Retain the last-known-good version when an update fails.
- Expose source, version, update time and failure status for diagnostics.

The application should not require a new Zenith release whenever a filter rule changes.

## 4. Filtering Scope

The initial implementation should prioritize network-request filtering. Cosmetic filtering and site-specific scriptlets may be added only after their maintenance and security costs are understood.

Highly dynamic sites, including video platforms, should be handled through updateable rules rather than hard-coded application logic.

## 5. User Control

Trusted list sources are selected in the Vault. Changing those sources is a Policy Change when it can alter durable access behavior.

Zenith should explain whether a failure came from site policy, a Blacklist source or a resource filter; these are different decisions.
