# Real-provider MFA acceptance test

Status: **Not run.** A synthetic password/OTP fixture is not real-provider evidence.
No provider compatibility or primary-account approval follows from this document.

## Preconditions

- Tester chooses the provider and supplies a disposable account with no valuable
  mail, contacts, payment methods, recovery role or organization privileges.
- Use a separate test Windows user or disposable VM. A copied Zenith executable
  is not profile isolation: application data is scoped to the Windows user.
- Enter passwords, OTPs, passkey PINs and recovery codes directly in the provider
  UI. Never send them to an agent, script, issue, test log or screenshot.
- Use the ordinary Release build, normal production guards, valid HTTPS and the
  normal Vault flow. No debug protocol attachment, automatic DOM capture, HAR,
  TLS interception, certificate bypass, host mapping or user-agent spoofing.
- Record commit plus dirty-state/build hash, Windows version, and the **running**
  WebView2 version from Settings. Run `tools/security/Invoke-SecurityChecks.ps1`
  for separate synthetic evidence. Its historical strict WebSocket criterion
  still fails, but F01 alone is no longer a release blocker. Assess failures
  against `security-review.md`: credential/origin/host/TLS, actual navigation,
  sensitive capabilities and document-removal defects remain relevant.
- Keep test-account recovery available outside Zenith. Do not enroll a primary
  security key or change a real organization's conditional-access policy.

## Provider selection

| Provider | Starting address | Scope to observe, not automatically authorize |
| --- | --- | --- |
| Google / Gmail | `https://mail.google.com/` | Exact login and callback hostnames reached by the chosen flow |
| Microsoft personal / Outlook | `https://outlook.live.com/` | Exact personal-account login and callback hostnames |
| Microsoft organization / Entra | Organization's approved test entry point | Tenant-approved consent, device and MFA requirements |

Do not add a broad parent such as `google.com` merely to accommodate sign-in.
Record a denied **hostname only**, obtain a deliberate test-profile Vault change,
then repeat the original step. A provider's embedded-browser refusal is a
compatibility failure, not a reason to evade its restrictions. Google explicitly
restricts OAuth requests in embedded user agents in its
[OAuth policies](https://developers.google.com/identity/protocols/oauth2/policies).
Website sign-in and third-party OAuth are separate paths: passing one does not
approve the other. Consult the provider's supported flow before testing a new one.

## Procedure and result sheet

Use one copy of this table per provider/account type and MFA method. Record only
Pass, Fail, Blocked, Not run, or Not applicable with a non-secret reason.
An unavailable method is not a pass. A method required for the intended account
blocks support for that flow; an unrelated provider or unused MFA method does
not block every account. Provider refusal is not itself a Zenith vulnerability.

| ID | Action and expected observation | Result |
| --- | --- | --- |
| MFA-01 | Open the direct mail address, then its sign-in link. Native origin caption matches the current HTTPS provider at each credential step; no tokens in caption. | Not run |
| MFA-02 | Cancel before password submission, return to the Sphere, reopen. No completed sign-in or stuck challenge. | Not run |
| MFA-03 | Use one deliberately incorrect password, then the correct disposable password. Provider error/retry works; no Zenith auto-save/autofill prompt. Stop if provider rate-limits. | Not run |
| MFA-04 | Complete authenticator OTP; test one incorrect/expired OTP separately. Provider, not Zenith, verifies it. | Not run |
| MFA-05 | Where supported, cancel/deny a push, then start a fresh attempt and approve the intended request. No unexpected approvals. | Not run |
| MFA-06 | Where supported, invoke a disposable passkey/security key; cancel, retry and complete. Record native/provider denial as blocked, never loosen device policy to pass. | Not run |
| MFA-07 | Test sign-in redirected from a permitted service and any required popup flow separately. Callback respects exact host policy; unknown callback gives a native boundary. | Not run |
| MFA-08 | Open a second tab; shared in-profile session is expected. Sign out and check both tabs. Browser close alone is NOT sign-out. | Not run |
| MFA-09 | Restart Zenith while an MFA challenge is pending; it must not turn into an authenticated session without provider verification. | Not run |
| MFA-10 | Restart after sign-in and record provider-selected session persistence. Verify separate test Windows profile does not inherit it. | Not run |
| MFA-11 | In the test profile, revoke the destination through the normal Vault flow. Retained page is unloaded; back/reload/new tabs cannot revive access. Restore only through normal policy change. | Not run |
| MFA-12 | Run confirmed Clear browsing data; Zenith closes. Relaunch and check sign-in is required; Vault policy/bookmarks remain. Revoke test sessions at the provider separately. | Not run |
| MFA-13 | For organization accounts, test approved consent, conditional access, device requirements and cancel/recovery with the tenant owner. | Not run |

Stop on an unexpected origin, certificate error, external launch/device prompt,
secret appearing in native diagnostics, or loss of policy enforcement. Record
only the step ID, normalized origin, generic error category, runtime and result.
Do not copy complete redirect URLs: query strings and fragments may be credentials.

## Sign-off

- Tester / date: unassigned
- Provider and account type (no email address): pending
- Commit, dirty state, assembly hash, Windows, runtime: pending
- MFA methods actually exercised: none
- Required exact-host policy changes (no URL paths/tokens): pending
- Failed/blocked steps and issue IDs: pending
- Sanitized evidence reviewed by a second person: pending
- Independent reviewer disposition: pending
