# ADR 0010: Mandatory synchronized host Blacklist

- Status: Accepted development checkpoint
- Date: 2026-09-07

The user selected [StevenBlack unified hosts + fakenews + gambling + porn](https://github.com/StevenBlack/hosts/tree/master/alternates/fakenews-gambling-porn), excluding social media. The source is fixed to `https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/fakenews-gambling-porn/hosts`. There is no source editor, disable switch or Vault override. Source corrections arrive via normal validated list replacement, not local exceptions. Source licenses and attribution remain in the downloaded hosts comments; no list is bundled or system hosts file modified.

Core parses hosts data, rejects malformed non-boilerplate records and builds an immutable exact-host index. No implicit wildcard semantics are added. The index is referenced by the effective navigation snapshot and resource-request policy; its precedence is independent of Vault mutations. Missing data fails closed. Timing edits and password recovery are not shortcuts around the overlay.

App downloads over HTTPS without redirects, at most 32 MiB and within 45 seconds including streamed content. It requires the source title, valid hosts syntax, 10,000–1,000,000 unique hosts, and rejects drops below half the preceding count. These are corruption/truncation checks, not cryptographic publisher authentication. Major legitimate upstream format/size changes require a reviewed application change; failed updates retain the prior list. A SHA-256 digest is diagnostic, not a signature.

Validated data is stored in a separate CurrentUser-DPAPI envelope with source and timestamp. Flush and atomic replacement precede in-memory publication; the previous envelope is retained, but a corrupt current cache is not automatically rolled back. The background loop checks every hour, fetching after 24 hours from success (or when clock rollback makes freshness uncertain). First use requires connectivity; later offline runs use the valid cache indefinitely with visible update failures. Stronger freshness/anti-rollback guarantees remain out of scope.

Every initialized tab installs WebView2 request interception for all supported source kinds and resource contexts. Listed requests are answered locally with an empty non-cacheable 403. HTTP(S) frame compatibility does not override this filter. Effective host-set changes unload retained pages/frame trees to remove content loaded before the update; unchanged sets merely refresh metadata. This conservative behavior can interrupt browsing and is stated in the UI/docs.

Tests cover exact matching, deceptive names, invalid input, mandatory precedence, proposal rechecks, daily scheduling, offline cache, failed downloads/writes, corruption, real frame/fetch/image blocking and update-time unloading. An opt-in test validates the actual selected upstream variant with a temporary cache. These are not a claim of comprehensive WebSocket/service-worker/cache interception or a substitute for independent network-level bypass testing.

Full ad blocking should use a reviewed compatible rule engine and updateable resource lists, not grow this hostname parser into a partial adblock-language implementation. Cosmetic rules and scriptlets need separate browser-context and safety review. Ad-filter exceptions must never defeat mandatory Blacklist decisions.
