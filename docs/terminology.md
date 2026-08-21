# Zenith Terminology

These terms form Zenith's canonical product vocabulary. Use the exact spelling and capitalization in documentation, code naming conventions and user-facing text.

## Whitelist

The durable set of sites that are directly accessible during ordinary browsing.

A site enters or leaves the Whitelist only through a confirmed Policy Change. Whitelist membership is never inferred from browsing history and is never created by an Access Grant.

## Blacklist

The set of sites that are inaccessible while classified there.

A Blacklisted site cannot be opened through an Access Grant. The Blacklist may contain locally chosen entries and entries supplied by trusted external lists.

## Greylist

The default class for every site that is neither Whitelisted nor Blacklisted.

The Greylist is the unclassified remainder of the Internet, not necessarily a stored list. A Greylisted site requires the deliberate-access procedure before it can receive an Access Grant.

## Vault

The protected control plane containing Zenith's durable access policy.

The Vault governs classifications, cooldowns, authentication settings and other access-affecting rules. It is not an ordinary settings page: access-affecting edits are staged as Policy Changes and do not take effect immediately.

## Access Grant

A limited authorization to access a Greylisted site after completing the required password–cooldown–password sequence.

An Access Grant does not alter the Whitelist, Blacklist or Greylist and cannot authorize a Blacklisted site.

## Access Class

The durable classification of a site that determines what navigation behavior applies.

Possible values:
- Whitelist
- Blacklist
- Greylist

Access Class is distinct from temporary runtime state such as Access Grants.

## Policy Change

A staged request to alter durable policy held by the Vault.

A Policy Change becomes effective only after its long waiting period has elapsed and the exact pending change is authenticated and confirmed. Until then, the previous policy remains active.

## Usage Rules

- Capitalize all six terms when referring to Zenith concepts.
- Use **Greylist**, not “no-list,” “grey zone” or “unknown list.”
- Use **Access Grant**, not “temporary Whitelist” or “bypass.”
- Use **Policy Change**, not “settings edit,” when durable access policy is altered.
- Use **Vault**, not “settings,” when referring to Zenith's protected policy controls.
- Implementation identifiers should preserve the same concepts, for example `AccessClass.Whitelist` and `PolicyChange`.
