# Zenith Product Principles

## 1. The Core Inversion

Conventional browsers provide access to the entire Internet by default. Restrictions are added afterward through blockers, extensions or self-imposed rules.

Zenith reverses this relationship:

> The Internet is unavailable by default. The user deliberately constructs the part of it that is accessible.

Zenith is therefore not an unrestricted browser with an optional focus mode. Its access policy is the foundation of the browser.

---

## 2. The Three Access States

Every site has one effective classification state, which determines what access procedures may apply.

### Whitelist

Whitelisted sites form the user’s ordinary Internet.

They have been deliberately selected and are directly accessible. The start surface presents these sites as the user’s normal browsing environment.

A site is Whitelisted because it was explicitly included, not merely because it was never blocked.

The interface presents this ordinary browsing environment as the **Sphere**. Sphere is user-facing experience vocabulary; it does not change or replace the Whitelist policy class.

### Blacklist

Blacklisted sites are deliberately excluded.

They cannot be opened while they remain Blacklisted, including through the Greylist procedure. The Blacklist may contain user-selected entries and entries obtained from trusted external block lists.

The user should not be able to remove sites from the Blacklist.

### Greylist

The Greylist is the remainder of the Internet: every site that is neither Whitelisted nor Blacklisted.

Greylisted sites are not part of ordinary browsing, but neither are they permanently prohibited. They may be accessed only through a deliberate exception procedure.

This makes the Greylist the default state for an unknown site.

---

## 3. Deliberate Greylist Access

Accessing a Greylisted site requires a time-separated sequence:

1. Request access.
2. Complete the first password challenge.
3. Wait for the configured cooldown.
4. Complete the second password challenge.
5. Receive a limited access grant.

The purpose of this sequence is to distinguish persistent intention from momentary impulse.

The first password makes the request explicit. The cooldown creates distance from the original impulse. The second password requires the user to confirm that the intention remains.

Completing this procedure does not add the site to the Whitelist. It grants only the limited exception defined by the site policy. 

---

## 4. The Vault

The Vault is the protected heart of Zenith's durable policy.

It defines the browser’s durable access policy, including:

* Whitelisted sites.
* Blacklisted sites and external list sources.
* Greylist cooldowns.
* Authentication settings.
* The scope of exceptional access.

The Vault is deliberately separated from ordinary browsing. A website, blocked page or momentary navigation request cannot modify it.

Vault changes must not take effect immediately. An access-affecting change follows a staged process:

1. The user authenticates and proposes the exact change.
2. Zenith records the change as pending.
3. A long cooldown—potentially several days—begins.
4. The existing policy remains active throughout the cooldown.
5. After the cooldown, the user must return to the Vault, authenticate again and confirm the pending change.
6. Only then does the new policy take effect.

If the change is altered, the waiting period begins again. An unconfirmed change never takes effect.

This prevents a passing impulse from changing the rules that are supposed to constrain that impulse. A user who suddenly wants permanent access to a Greylisted site cannot simply open the settings and Whitelist it immediately.

The Greylist procedure remains available for deliberately gated exceptional access. Changing the browser’s permanent policy requires the much longer Vault process.

Exact waiting periods and the treatment of different change types belong in `site-policy.md`.

---

## 5. Friction Is Part of the Design

Most browsers treat every delay between desire and access as a defect. Zenith does not.

Zenith uses deliberate friction at two different levels:

* **Greylist friction** slows an exceptional visit.
* **Vault friction** protects the browser’s long-term rules from impulsive change.

These mechanisms are not punishment. They create time for an immediate desire to weaken or disappear before access is granted.

Convenience is valuable only when it does not undermine the user’s prior decisions.

---

## 6. Exceptions Are Not Policy Changes

A temporary exception and a permanent policy change are fundamentally different actions.

---

## 7. Policy Before Browsing

A site must be classified before navigation is permitted.

WebView2 provides the rendering engine, but it does not decide what the user may access. Links, redirects, popups and other navigation paths must remain subject to Zenith’s policy.

When Zenith cannot determine that access is permitted, it should not navigate.

Restrictions must be predictable. When navigation is delayed or denied, Zenith should explain the relevant policy. Detailed reasons for permitted navigation may remain available on demand without adding classification labels to routine chrome.

---

## 8. User Authority

Zenith represents the user’s considered policy, not the goals of websites.

A website is not entitled to access because:

* It was linked from an allowed page.
* It requested a permission.
* It would be more convenient to allow it.
* Blocking it would reduce compatibility.
* It attempts to open through another navigation mechanism.

The user determines which part of the Internet exists inside Zenith.

---

## 9. Product Boundary

Zenith is not merely:

* A productivity browser.
* A website blocker.
* An ad blocker.
* A collection of focus tools.
* An unrestricted browser with additional controls.

It is a browser built around a different assumption:

> Access is granted, not presumed.

When evaluating a feature, ask:

> Does this preserve the Internet the user deliberately constructed, or does it recreate unrestricted access by another route?
