# Microsoft WebView2 secure-hosting baseline

Reviewed 2026-09-10 against Microsoft's
[Develop secure WebView2 apps](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)
(page dated 2026-07-16). This is an implementation review against that guidance,
not a Microsoft certification, independent audit or guarantee of account safety.

| Recommendation | Zenith implementation and evidence |
| --- | --- |
| Treat content and incoming messages as untrusted | `CosmeticMessage` checks message kind, native sender URL, scheme, token and array/payload bounds. Tests reject malformed messages, forged origins and oversized fields. |
| Check the current document before sending data | Main-frame messages must also match the current engine `Source`; stale messages are dropped. Frame replies use the originating native frame, not the selected tab. The cosmetic script accepts only its own document token and URL. Replies contain presentation CSS, never credentials or private host state. |
| Carefully scope document-created scripts | The sole production injected script is bundled `cosmetics.js`. It exchanges bounded DOM hints and CSS, has no host objects, file access or Vault/authentication services. No production `ExecuteScript` calls were found. |
| Avoid generic native proxies | The cosmetic interface has one fixed operation. There is no arbitrary native-method dispatcher or general-purpose host object. Microsoft's wording here concerns native API bridges, not network proxies. |
| Serialize outgoing messages as JSON | `CosmeticFilterGuard` uses `JsonSerializer.Serialize` and native `PostWebMessageAsJson`, not interpolation into executable JavaScript. |
| Disable functionality that is not needed | Host objects are disabled. Web messaging is enabled only when cosmetic filtering needs it. Default JavaScript dialogs are disabled. JavaScript itself stays enabled because Zenith renders interactive websites; the guidance makes this conditional on need. |
| Re-evaluate navigation/settings as content changes | Main navigation, popups and request-stage frame documents route through Core. The host/capability restrictions apply to every initialized tab, so moving between sites does not expose a more privileged settings profile. Cross-origin frames keep the agreed embedded-content policy. Native tests exercise these paths. |
| Remove exposed host objects on navigation | No host objects are added and `AreHostObjectsAllowed` is false, so there is no exposed object to remove. Future additions require a new review. |
| Use a standard non-elevated host, never SYSTEM | Explicit `asInvoker`, `uiAccess=false` manifest; application startup refuses an enabled administrator token, SYSTEM, or an unreadable identity before opening policy stores or creating WebView2. A filtered UAC token may run normally. Unit tests cover the eligibility combinations. |

## Focused changes from this review

- Added explicit launch privilege enforcement. No automatic elevation or relaunch,
  and no Windows configuration changes. Reopen normally if launched as administrator.
- Extracted the existing cosmetic message checks into a testable presentation
  protocol; rejected empty tokens and stale top-level document messages.
- Preserved normal website scripting and existing authentication/navigation rules.
  No connection gateway, certificate bypass or additional blanket capability ban.

## Verification and limits

Run Debug/Release builds and the ordinary suite with `ZENITH_WEBVIEW_TESTS=1`
and `ZENITH_STRICT_CONNECTION_TESTS=0`. Native regressions include cosmetic
filtering/nested frames and disabled host objects/messages without the cosmetic
bridge. Privilege eligibility and message validation have targeted unit tests.
The refused elevated-launch dialog is not an automated end-to-end token/UAC test.

The separate strict connection gate still fails SEC-CONN-001. Under the
2026-09-12 product clarification, this is an Informational resource-filtering
coverage limitation, not an account-security release blocker or demonstrated
navigation bypass, credential theft, TLS break or sandbox escape. The historical
test criterion remains unchanged; see [connection coverage](connection-coverage.md)
and the [reassessment of all original findings](security-review.md).
Real-provider compatibility and
independent review remain separate work, not requirements asserted by this
Microsoft article for every WebView2 application.

Revisit this checklist when adding native bridges, privileged operations, scripts,
new browser capabilities or deployment changes. Keep the WebView2 runtime current.
