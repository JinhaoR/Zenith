# Zenith

**A Windows browser where Internet access is granted, not presumed.**

Conventional browsers begin with the entire Internet available and ask users to block what they do not want. Zenith reverses that model: nothing is directly accessible unless policy provides a deliberate path to it.

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

Zenith is in early development and is not yet ready for everyday use. Phase 1 is complete: the Sphere-first WPF shell and centralized fail-closed navigation boundary are implemented. Phase 2 Site Policy is next.

## Technology

- C# and .NET
- WPF
- Microsoft WebView2
- A platform-independent Core for policy and domain logic

## Build

On Windows, with the required .NET SDK installed:

~~~powershell
dotnet restore Zenith.slnx
dotnet build Zenith.slnx
dotnet test Zenith.slnx
~~~

## Run

~~~powershell
dotnet run --project src/Zenith.App/Zenith.App.csproj
~~~

Until Site Policy is implemented, the shell deliberately denies all external navigation.

## Documentation

- [Product principles](docs/product-principles.md)
- [Product experience](docs/product-experience.md)
- [Terminology](docs/terminology.md)
- [Site policy](docs/site-policy.md)
- [Architecture](ARCHITECTURE.md)
- [Threat model](docs/threat-model.md)
- [Roadmap](ROADMAP.md)
