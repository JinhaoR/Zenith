# Connection and resource-filtering evidence

2026-09-12 reassessment: Zenith controls visible website/document access and
protects authenticated browsing; it is not a network firewall. F01/SEC-CONN-001
is **Informational**, not a primary-account release blocker. Ordinary background
communication is not a site-access violation simply because of its destination.
This ledger preserves earlier filtering experiments, including a failing test
of a stronger zero-contact requirement. See `security-review.md` for the current
release priorities. Existing filters and test implementations were not changed.
The 2026-09-09 loopback run used WebView2 **152.0.4191.66** and the dirty development
tree. Reproduce against the exact candidate binary before release.

## Reproduce

```powershell
dotnet restore Zenith.slnx
./tools/security/Invoke-SecurityChecks.ps1
```

The runner saves build/test logs, candidate assembly SHA-256 and environment
metadata under ignored `bin/security-checks/`. It first runs normal regressions,
then runs the historical strict connection gate separately. A failed strict gate
still returns exit code 1 even when ordinary regressions pass. Its F01 assertion
does not represent the clarified account-security acceptance criterion; updating
runner/test wording is follow-up work, not a claimed passing result. Provider MFA and independent
review cannot be approved by this script. Manually inspect evidence before sharing.

The normal native suite includes exploratory WebSocket observations so later
security/lifecycle regressions still run; it still prints an obsolete release-blocker
message when contact occurs. For a standalone stronger-model observation, set both
`ZENITH_WEBVIEW_TESTS=1` and `ZENITH_STRICT_CONNECTION_TESTS=1`, then run
`WebViewNavigationTests` with normal console verbosity. Do not use a green normal
suite alone as release evidence.

## Coverage and unresolved work

| Path | Evidence / status | Further investigation / applicable acceptance |
| --- | --- | --- |
| HTTP redirect, POST, link, popup, subsequent tab | Existing server-observed denial regressions | Repeat against candidate/runtime |
| Fetch, XHR, keepalive POST, beacon, EventSource | New positive controls reached receiver; combined transport/mandatory-host denial had local 403 and no receiving-server request | Public valid-TLS coverage; keepalive during actual unload |
| `srcdoc` / inherited blank frame fetch | New same-API positive controls plus denied request absence | Navigation, nested and out-of-process variants; no claim of general frame isolation |
| Dedicated/shared/service workers | Existing targeted native tests | Startup before cleanup, worker socket traffic, full cross-tab/restart matrix |
| WebSocket handshake | **Informational: SEC-CONN-001**, filtered hostname reached receiver; historical assertion fails | Preserve coverage limit; test socket-driven navigation/host-message boundaries, not mandatory zero contact |
| Already-established WebSocket / SSE / streaming fetch | Not tested | Revocation, expiry, tab close, renderer failure and process exit must be observed at server |
| WebTransport / HTTP3 / QUIC | Not tested | Optional filtering research; no blanket egress-denial acceptance requirement |
| WebRTC data channels / ICE / STUN | Not tested | Distinguish ordinary networking from camera/microphone/screen exposure; sensitive permissions remain relevant |
| DNS-prefetch / preconnect / speculative traffic | Not tested | Optional privacy/filtering research; not a navigation bypass by itself |
| OOPIF, cache/back-forward restoration | Partial existing document tests only | Prove target process/frame routing and retained-page revalidation |

The new HTTP probes compare a literal-loopback control with a mapped denied DNS
hostname; they exercise combined transport/mandatory protection, not independent
proof of each rule. EventSource controls prove the HTTP request reaches the server,
not a long-lived SSE session. Keepalive controls run while the page remains alive.

## SEC-CONN-001: WebSocket request interception gap

- Severity: **Informational** under the clarified product model; previously High
  under a stronger network-isolation requirement. Closed as an account-security
  blocker; retained as a known filtering limitation, not fixed in implementation.
- Source: implementation-agent dynamic testing, not an independent audit.
- Reproduction: a Whitelisted loopback page creates `WebSocket` to
  `ws://blocked.zenith.test:<ephemeral-port>/socket-denied`, mapped only in the
  isolated test environment to a loopback receiver. The hostname is in the fixed
  test Blacklist; Core would deny both its host and its public `ws:` transport.
- Observed: receiver recorded the HTTP upgrade request. A reachable allowed
  WebSocket control was observed first. The receiver intentionally rejects the
  handshake, so JavaScript receives an error **after network contact**.
- Impact proven in the original experiment: network contact that the resource
  filter would deny if invoked, not a bypass of Core navigation decisions. Payload
  exchange, WSS and persistent-stream exploitation were not tested and are not
  claimed. Do not infer that a JavaScript error or UI boundary stopped contact.
- Affected boundary: App's `NetworkSafetyGuard` / `ResourceRequestGuard` rely on
  native request callbacks; `DocumentRequestGuard` intercepts documents only.
  Merely having a Websocket enum branch is not proof that a callback is emitted.
- Next step: retain the measured coverage limit and align future release tests
  with actual navigation, host/origin, credential, capability and lifecycle
  guarantees. A gateway or OS firewall is not required to close this finding's
  former account-security classification. JavaScript monkey-patching is not a
  security boundary and is not proposed.
- Later isolated 2026-09-12 probes confirmed WS upgrades and WSS TCP contact from
  documents, frames/OOPIFs and workers. TLS validation remained active. These
  mechanism experiments do not prove credential theft or policy-state mutation.

### Native blocking experiment, 2026-09-09

On WebView2 152.0.4191.66, calling `Network.setBlockedURLs` before the denied
WebSocket probe with both `ws://blocked.zenith.test/*` and
`ws://blocked.zenith.test:*/*` succeeded as a protocol command, but the receiving
server still observed `/socket-denied`. The strict gate failed again. The
experimental call was removed; it is not shipped as a protection.

Historical stronger-model candidate, **not required or implemented**: a loopback-only,
application-owned WebSocket connection gateway that checks Core transport and
mandatory-host rules before DNS/connect, and tunnels permitted TLS without
decrypting it. Chromium's manual proxy mapping can route WebSockets to the
"other proxies" entry, separately from HTTP/HTTPS page requests. A first version
using direct HTTP/HTTPS mappings would not preserve system/corporate proxy
behavior. User approval is required for that compatibility change. Windows proxy
settings, certificate stores and other applications must not be modified.

This candidate still needs a reviewed parser/lifecycle design, bounded resources,
fail-closed routing, no direct fallback, loopback-bypass handling, and server-side
WS/WSS/frame/worker/update tests before any remediation claim. Background-source
identity and other non-WebSocket transports must not be claimed covered by a
host-only gateway.

- [CDP Network.setBlockedURLs](https://chromedevtools.github.io/devtools-protocol/tot/Network/#method-setBlockedURLs)
- [Chromium proxy mappings and WebSocket selection](https://chromium.googlesource.com/chromium/src/+/main/net/docs/proxy.md)
