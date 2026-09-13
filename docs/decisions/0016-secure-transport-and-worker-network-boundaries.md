# ADR 0016: Secure transport and worker network boundaries

Date: 2026-09-09

Status: Accepted development checkpoint; not primary-account certification.

2026-09-12 scope clarification: this ADR records implemented transport and worker
restrictions, not a general network-firewall product requirement. The current
threat model permits ordinary background communication by authorized websites.
Zero startup/contact guarantees and the F01 gateway proposal are not prerequisites
for account safety. Existing restrictions are unchanged; further changes to them
require their own behavior review. See `../security-review.md` for revised findings.

## Context

The user approved continuing HTTPS-only browsing, broader worker protection,
provider sign-in/MFA testing and independent-review preparation. This supersedes
the pending HTTPS decision in ADRs 0014/0015. Site/transport policy belongs in
Core, not a browser setting that the Vault or a filter exception can weaken.

## Decision

Core rejects public HTTP navigation, including otherwise-authorized destinations,
frames and credential-bearing redirects. It never sends HTTP to discover an
upgrade. Bare addresses still default to HTTPS. Literal loopback development
addresses retain HTTP eligibility, not automatic site authorization. DNS names
and LAN hosts are not loopback exceptions. Resource filtering and the independent
native safety guard reuse this transport rule; intercepted public WS is denied.
The existing certificate-denial policy remains mandatory.

`NetworkSafetyGuard` uses the native `RequestedSourceKind`, not Referer, page
messages or the selected tab. Core denies shared/service-worker and unknown
sources; document sources (including dedicated workers) receive transport and
resource filtering. These are capability restrictions, not new site classes.

Native experiments exposed two important limits: a separate empty controller
did not intercept the tested worker fetches, and an older persisted service
worker could send traffic outside a newly installed listener. The unused
auxiliary-controller design was removed. Before any tab becomes ready, a single
session task calls the supported profile cleanup for `ServiceWorkers`, terminating
and unregistering legacy workers. Required cleanup failure closes the window.
Cookies, local storage, passwords and Vault data are not cleanup targets.

Each browsing controller installs the safety filter before navigation. Microsoft
recommends one worker listener to avoid duplicate delivery; this checkpoint
deliberately uses identical, unconditional worker denials on every tab rather
than a context-dependent allowance or a gap during listener handoff. No handler
releases a denied request or treats another tab as its owner. Duplicate callbacks
can cost performance; consolidate only with an equivalent tested lifecycle.
Document resource filtering ignores non-document sources rather than evaluating
them against a page URL. Closing the original tab does not remove the remaining
tab's protections.

## Compatibility and limits

Settings explains HTTPS requirements and unavailable worker-backed offline and
background features. Worker registration can fail on otherwise-allowed sites.
Shared workers may still execute local code. No permission/Vault override or
JavaScript monkey-patch is added. No user-agent spoofing, certificate exception,
or automatic authorization of identity-provider hosts is used to make login work.

Cleanup occurs after creating the rendering environment and before ready state;
this is not proof of zero startup network contact. Out-of-process targets,
WebSockets, speculative traffic, established connections, file-system handles and
runtime failure combinations require additional review. A package audit does not
cover these behaviors.

## Verification scope

- Core tests cover HTTP denial across origins, valid grants, embedded compatibility,
  Blacklist precedence, loopback/lookalike boundaries, resource exceptions and
  unknown worker sources.
- An isolated profile is seeded with a service worker, then the browser process
  exits. Zenith removes that registration while retaining a persistent test cookie
  and local storage. New worker registration is denied without reaching the server.
- A shared worker attempts a fetch and receives a local denial; an independent
  loopback server records no request. Dedicated-worker insecure fetches and
  protections after closing the original tab are exercised separately.
- Controlled form-password/OTP submissions use fake fixture values only.
  Cross-origin allowed redirects are distinguished from denied 307/308 POST
  forwarding. A server observer verifies denied destinations receive no request.
  These HTTP loopback fixtures do not validate public-provider TLS/MFA or implement
  OAuth state/nonce/PKCE validation on behalf of websites.
- Existing certificate, native capability, cookie-isolation, Vault and confirmed
  profile-cleanup regressions remain required.

External review has **not** occurred. See `../security-review.md` for the handoff.

## References

- [Microsoft: request source kinds](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webresourcerequestsourcekinds)
- [Microsoft: request filters and worker delivery](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.addwebresourcerequestedfilter)
- [Microsoft: browsing data kinds](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2browsingdatakinds)
- Native signatures and the termination/deregistration semantics of `ServiceWorkers`
  were checked against the pinned 1.0.4129.50 SDK XML reference.
