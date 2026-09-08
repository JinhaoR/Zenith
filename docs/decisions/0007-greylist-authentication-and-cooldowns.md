# ADR 0007: Greylist Authentication and Cooldowns

- **Status:** Accepted development design
- **Date:** 2026-09-06

## Confirmed product choices

Historical Phase 3 values below were subsequently changed for development testing under ADR 0008. Current timing rules and password-change behavior are owned by `site-policy.md`.

Both challenges use the same initially configured password. The first successful challenge starts a 30-minute cooldown. The second successful challenge, after eligibility, issues 60 minutes of access for the exact normalized hostname across tabs. Grants are memory-only and end at expiry or application exit. Cooldowns persist across restarts; no subdomains are included implicitly.

## Design and review

- Password verification uses the framework's PBKDF2-HMAC-SHA256 implementation, 600,000 iterations, a random 32-byte salt and a 32-byte verifier, compared with `CryptographicOperations.FixedTimeEquals`. The password is not persisted or logged. This follows the PBKDF2 work factor documented by [OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).
- The verifier and cooldown snapshot share one versioned envelope protected by Windows DPAPI with `DataProtectionScope.CurrentUser`. Use the [framework DPAPI API](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection), not application-defined encryption or key storage.
- Save a complete envelope to a temporary sibling file, flush it, then atomically replace the current file. Successful authentication cannot advance access until its state write succeeds. An exclusive file lease prevents two app instances from independently updating temporary-access state.
- One-time password setup requires 15–128 characters and confirmation. A separate initialization marker prevents a missing credential/state file from silently becoming a fresh setup after initialization. Failed, unreadable, corrupt or inconsistent state disables temporary access. There is no password reset, credential replacement or recovery bypass in this phase; those need protected Vault design.
- Core owns state transitions, classification checks, grant issuance and validity. App implements password verification and protected persistence behind interfaces. A submitted target is normalized and classified again before either challenge; Blacklist always wins.
- UTC deadlines preserve cooldown progress while the app is closed. During a running session, compare UTC to monotonic elapsed time; material clock jumps or rollback below the persisted high-water mark disable temporary access for that session. Correcting the clock and restarting allows revalidation. Grants do not survive restart and cannot revive after a detected clock anomaly.
- Without trusted external time, a clock advanced while the app is closed or a restored complete older local security snapshot cannot be reliably detected. This development design is bounded by the documented local-administrator threat limit; it is not hardened anti-tamper enforcement. Stronger offline clock/snapshot protection remains a security review item.
- Failed password attempts introduce a persisted five-second retry delay. UI submissions are serialized and password fields are cleared immediately after capture; only the verification operation retains the transient input.
- A dispatcher timer rechecks displayed and retained tabs against Core; tab activation also rechecks before visibility. Expired or revoked grants hide and unload their pages; returning to the Sphere also unloads content. Every subsequent link, redirect and popup still passes through the common evaluator. Allowed decisions retain the resolved Access Class so temporary authorization never becomes Whitelist-backed discovery or bookmarking.

## Verification requirements

Test first and second authentication, rejection before eligibility, incorrect passwords, restart during cooldown, consumed cooldowns after restart, exact hostname scope, expiry, Blacklist precedence, failed persistence, corrupt/missing initialized state and clock anomalies. Exercise the real WPF boundary and settings flow with a fake clock and isolated state. Never shorten production waiting periods for UI tests.
