# Threat Model

## 1. Security Goal

Zenith must enforce the user's previously chosen access policy consistently, including when the user experiences a later impulse to weaken it.

Zenith protects the integrity of:

- Site classification.
- Vault policy.
- Pending Policy Changes.
- Greylist cooldowns.
- Access Grants.
- Authentication material.

## 2. Relevant Threats

Threats include:

- A website attempting to escape its classification.
- A navigation path bypassing the central evaluator.
- Redirects, popups, embedded frames or external schemes reaching restricted content.
- Deceptive hostnames, subdomains, internationalized domains or malformed URLs.
- Restarting Zenith or altering the system clock to shorten a delay.
- Corrupt, missing or partially written policy state.
- A filter-list update that is unavailable, malformed or compromised.
- Sensitive values appearing in logs or diagnostics.
- Web content attempting to invoke privileged host functionality.

## 3. Trust Boundaries

- WebView2 and all rendered web content are untrusted with respect to Zenith policy.
- Zenith.Core is the authority for access decisions.
- The Vault is the authority for durable policy.
- External lists are untrusted input until validated.
- The system clock alone must not be trusted to prove that a delay elapsed.

## 4. Required Mitigations

- Route every navigation mechanism through common policy logic.
- Evaluate policy before allowing content to load.
- Normalize and compare URIs structurally.
- Persist cooldowns, Access Grants and Policy Changes safely.
- Apply policy writes atomically and retain a recoverable last-known-good state.
- Fail closed when policy or authentication cannot be evaluated.
- Never store or log plaintext passwords.
- Validate external list formats and retain the last valid version after update failure.
- Test bypass paths as well as normal behavior.

## 5. Scope Limit

Zenith is not initially a hardened kiosk or operating-system access-control system. A user with administrative control of Windows may install another browser, modify application files or remove Zenith.

The initial promise is narrower: while using Zenith in its supported environment, Zenith must not undermine its own policy through ordinary browsing behavior or implementation inconsistencies.

Stronger tamper resistance may be considered later and must be documented as a separate security goal.
