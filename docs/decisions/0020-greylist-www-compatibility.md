# ADR 0020: Temporary access and the www counterpart

Date: 2026-09-19. Status: accepted by explicit user clarification.

## Context

The user reported that completed Greylist requests for youtube.com and google.com
did not open, and explicitly requested that typing `www` not be necessary. Tracing
the flow showed that both confirmations succeed, the pending request is consumed,
the session grant is created, and App navigates to the original URI. Native
navigation then rejects the site's redirect to its `www` hostname because the
previous grant covered only the requested hostname. Google was reproduced live;
YouTube showed the same redirect in the earlier investigation. No lost URI or
grant/navigation ordering failure was found in this path.

## Decision

Use the narrow Core-owned grant scope defined in site-policy.md. It supersedes
the exact-host-only scope in earlier Greylist ADRs for this one explicit pair.
Do not rewrite addresses, general site identity, Whitelist rules, embedded-content
policy or native navigation handlers. A redirect still crosses the ordinary Core
policy boundary. Existing exact grants remain exact unless explicitly constructed
with the compatibility scope; the Greylist service issues that scope after final
confirmation. The recorded request, captured cooldown, confirmation requirements,
grant expiry, session-only lifetime and Blacklist precedence are retained.

## Validation

Core regressions cover both directions, one shared expiry, optional authentication,
incorrect final authentication, invalid-state recovery, unrelated hosts, deceptive
names, IP addresses and Blacklisting either destination or the original host.
The native Greylist scenario enters a bare address through Settings, completes
both confirmations with passwords off and on, follows an intercepted HTTPS 302
response through the real WebView2 navigation events, and verifies the loaded DOM
and expiry clearing. Its unrelated-host redirects remain denied. Synthetic HTTPS
responses keep this regression deterministic without changing TLS validation.

After the change, isolated unsigned-in live WebView2 checks completed successfully
at both `www.google.com` and `www.youtube.com` using the corrected Core grant scope.
The full native suite also passed on WebView2 153.0.4234.32, including existing
F02 frame-document, F03 clearing and F06 file-chooser regressions.
