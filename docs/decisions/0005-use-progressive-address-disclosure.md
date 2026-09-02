# ADR 0005: Use Progressive Address Disclosure

- **Status:** Accepted
- **Date:** 2026-09-02

## Context

Zenith must make the identity of a rendered external page inspectable without turning a permanent conventional address bar into the visual center of the browser. The existing Sphere field already provides the secondary direct-navigation path.

## Decision

- While a permitted page is visible, the sidebar shows a compact current-site control containing its canonical hostname.
- The control's tooltip and accessibility help text expose the complete address.
- Activating the control, or pressing `Ctrl+L`, expands the sidebar and places the complete address in the existing Sphere field for inspection or editing.
- `Ctrl+K` keeps its separate meaning: clear the field and search the user's Sphere.
- The current-site control is absent on the Sphere and native boundary surfaces and never displays an Access Class badge during ordinary browsing.

## Consequences

Site identity is continuously available, full addresses are one deliberate action away, and Zenith retains one compact field rather than adding permanent top chrome. Directly entered destinations still receive normal Core policy evaluation.
