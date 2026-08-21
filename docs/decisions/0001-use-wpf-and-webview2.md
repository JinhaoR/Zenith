# ADR 0001: Use WPF and WebView2

- **Status:** Accepted
- **Date:** 2026-08-21

## Context

Zenith requires a Windows desktop shell, native application controls and a maintained modern web-rendering engine. Building or maintaining a browser engine is outside the project's purpose.

## Decision

Use WPF for the desktop application and Microsoft WebView2 for web rendering.

Zenith will treat WebView2 as an untrusted rendering component. Navigation and policy decisions remain in Zenith.Core rather than inside the rendering layer.

## Alternatives Considered

- WinUI 3.
- Chromium Embedded Framework.
- Electron.
- A custom browser engine.

## Consequences

### Positive

- Native C# and .NET application structure.
- Access to a maintained Chromium-based engine.
- Clear separation between Zenith policy and page rendering.

### Negative

- Zenith is Windows-specific.
- Some behavior depends on the installed WebView2 Runtime.
- Zenith must carefully intercept all relevant WebView2 navigation paths.
- Engine-level features remain outside Zenith's direct control.
