# Zenith

**A Windows browser where Internet access is granted, not presumed.**

Conventional browsers begin with the entire Internet available and ask users to block what they do not want. Zenith reverses that model: nothing is directly accessible unless policy provides a deliberate path to it.

To run this application use:

dotnet run --project src/Zenith.App/Zenith.App.csproj

## How It Works

Every site has one access classification:

- **Whitelist** — opens directly.
- **Blacklist** — cannot be opened.
- **Greylist** — requires deliberate, time-separated access.

~~~mermaid
flowchart TD
    Request["Site requested"] --> Classify{"Zenith policy"}

    Classify -->|Whitelist| Open["Open directly"]

    Classify -->|Blacklist| Block["Block"]

    Classify -->|Greylist| Gate["Password → cooldown → password"]

    Gate --> Grant["Access Grant issued"]
    Grant --> OpenTemp["Open temporarily"]

    OpenTemp --> Expire["Grant expires"]
    Expire --> Greylist["Site remains Greylisted"]
~~~

Sites not explicitly placed on the Whitelist or Blacklist belong to the Greylist by default.

Completing the Greylist procedure creates a limited **Access Grant**. It does not add the site to the Whitelist.

In the application, the ordinary browsing environment formed by Whitelisted sites is called your **Sphere**. Sphere is user-facing language; Whitelist remains the underlying policy classification.

## The Vault

The **Vault** contains Zenith's durable access policy.

Changing that policy is intentionally slow. An access-affecting edit becomes a **Policy Change**:

1. Propose and authenticate the change.
2. Wait through a long cooldown, potentially several days.
3. Return, authenticate again and confirm the exact change.

The existing policy remains active until confirmation. This prevents an immediate impulse from rewriting the rules intended to constrain it.

## Project Status

Zenith is in early development and is not yet ready for everyday use. Phases 1–4 are complete as development checkpoints: the browser shell uses normalized hostname identity, Core-owned site classification and temporary access, native navigation explanations, safe native-surface lifecycle handling, a searchable Sphere directory and protected Vault policy editing. Broader security hardening remains ahead.

Settings saves startup sidebar layout and default page zoom, provides a filterable Sphere directory, and supports password setup and resuming pending temporary-access requests. Settings → Vault stages password changes, timing changes and additions to your Sphere. Every proposal waits through the current Vault delay and requires confirmation with the current password.

For development testing, Greylist wait, visit duration and Vault wait initially use **five seconds**. Increase them in the Vault using seconds, minutes, hours or days. Shortening a delay still follows the old delay, and existing requests keep their recorded deadlines. Cooldowns and Vault proposals survive restart; grants do not. See [Site Policy](docs/site-policy.md) and [ADR 0008](docs/decisions/0008-staged-vault-policy.md) for migration rules and security limits. Testing values are not production-ready defaults.

## Filtering

Mandatory host protection uses StevenBlack unified hosts plus fake-news, gambling and porn (not social media). It cannot be disabled or overridden in the Vault. The first run needs a successful list download before browsing; later failed updates retain the last valid protected cache. Settings → About shows update health. Daily host-set changes unload open pages so newly blocked embedded content cannot remain active; reopen destinations afterward. See [ADR 0010](docs/decisions/0010-mandatory-synchronized-host-blacklist.md).

EasyList and EasyPrivacy add network and cosmetic filtering through the bundled
Ghostery engine. Updates are atomic and retain working snapshots on failure;
resource exceptions never override Blacklist. Settings → About shows list versions,
rule counts and filtering health. Scriptlets, shadow DOM and complete video-ad
removal are not implemented. See [filtering scope](docs/adblocking.md) and
[bundle maintenance](tools/adblock/README.md). End users do not need Node.

## Technology

- C# and .NET
- WPF
- Microsoft WebView2
- Ghostery filtering engine hosted in ClearScript V8
- A platform-independent Core for policy and domain logic

## Build

On Windows, with the required .NET SDK installed:

~~~powershell
dotnet restore Zenith.slnx
dotnet build Zenith.slnx
dotnet test Zenith.slnx
~~~

The default suite skips the renderer integration test. With the WebView2 runtime installed, run it explicitly to exercise first-open navigation, settings, both password challenges and grant expiry across actual WPF/WebView2 tabs. It uses invisible windows, a temporary browser profile, local test page responses and a fake clock without changing production waiting periods:

~~~powershell
$env:ZENITH_WEBVIEW_TESTS = '1'
dotnet test tests/Zenith.App.Tests/Zenith.App.Tests.csproj --filter Category=WebView2
Remove-Item Env:ZENITH_WEBVIEW_TESTS
~~~

For opt-in live subscription verification with temporary caches, set
`ZENITH_BLACKLIST_LIVE_TESTS=1` and `ZENITH_ADBLOCK_LIVE_TESTS=1` before running
the suite. Ordinary tests use bundled or synthetic list data without Internet
downloads. Renderer tests also cover resource blocking, cosmetic exceptions,
strict CSP, dynamic elements and nested frames.

The same renderer suite includes loopback-only HTTP servers that independently
record requests. It verifies denied redirects/script navigation/new-tab requests
do not reach those servers, while permitted redirects and widgets do. Test
hostname mapping is confined to the temporary browser profile; Windows hosts and
DNS settings are not modified. See [ADR 0012](docs/decisions/0012-document-request-gate-and-network-observation.md)
for the new document gate and its remaining coverage limits.

## Run

~~~powershell
dotnet run --project src/Zenith.App/Zenith.App.csproj
~~~

The development build initially opens its starter Whitelist directly. For another valid destination, deliberately enter its full address and choose the secondary temporary-access action. Initial password setup is available under Settings → Temporary access or Vault. Keep that password safe: changing it through the Vault requires the current password; forgotten-password recovery is not implemented. Unsupported targets and unreadable initialized policy fail closed.

## Documentation

- [Product principles](docs/product-principles.md)
- [Product experience](docs/product-experience.md)
- [Terminology](docs/terminology.md)
- [Site policy](docs/site-policy.md)
- [Architecture](ARCHITECTURE.md)
- [Threat model](docs/threat-model.md)
- [Roadmap](ROADMAP.md)
