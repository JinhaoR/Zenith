# Zenith Product Experience

## 1. Purpose

This document defines Zenith's intended user experience, interaction hierarchy and interface character.

It does not redefine Site Policy, permissions or security behavior. Those remain owned by their respective policy documents. The interface must express those rules without making enforcement feel like the center of the product.

The experience goal is:

> The Sphere should feel like the user's complete Internet, not like the unrestricted Internet with a blocker placed in front of it.

The user's ordinary Internet is intentionally smaller than the public Internet, but it should feel whole, useful and natural during everyday browsing.

---

## 2. The Sphere Is the Product

The **Sphere** is Zenith's user-facing name for the ordinary browsing environment formed by destinations whose current Access Class is Whitelist. In everyday use, it is simply where browsing happens, not a mode or policy status.

Sphere is an interface concept only. Whitelist remains the canonical Access Class in policy, Core code, persistence, tests and developer-facing diagnostics. Zenith must not introduce `AccessClass.Sphere`, store Sphere separately or make presentation state authoritative for access. A Greylisted destination opened under an Access Grant does not become part of the Sphere.

The interface should present sites in the user's Sphere as the Internet that exists inside Zenith. It should not continually remind the user that other sites are absent, restricted or waiting outside it. Sphere should name the overall environment when that environment needs a name; it should not become a badge repeated on every ordinary site.

This implies:

- Ordinary browsing emphasizes destinations, tasks and content rather than Access Classes.
- Policy enforcement is quiet while access is permitted.
- The start surface is an entrance to the user's Sphere, not a policy dashboard.
- Permanent lock indicators, warning banners and classification labels do not belong in routine browser chrome.
- Zenith should not measure its success by how often it tells the user that something was blocked.

---

## 3. Experience Hierarchy

The interface should reflect how often and how intentionally each part of Zenith is used.

| Experience layer | Purpose | Intended prominence |
| --- | --- | --- |
| Sphere | Reach familiar, directly accessible sites and continue normal work. | Primary and immediate. |
| Direct navigation tools | Open a known address or act on the current page. | Available on demand, but not visually dominant. |
| Greylist access | Request an unusual, temporary exception. | Secondary, deliberate and absent from the normal path. |
| Vault | Review or change durable access policy. | Located with protected settings and visited rarely. |

Prominence must not change policy strength. A less visible Greylist or Vault entry point remains accessible to a deliberate user, including through keyboard and assistive technology.

---

## 4. The Start Surface

The start surface should help the user move within their Sphere. It should not attempt to display every Whitelisted site as a desktop-sized grid.

It should use progressive layers:

1. **Continue and frequent destinations** - a small set of recently or commonly used sites.
2. **Bookmarks** - stable user-chosen shortcuts, shown compactly in the sidebar and never replaced by automatic ranking.
3. **Collections** - user-meaningful groups such as Research, Work, Reference, Communication or Reading.
4. **Find in your Sphere** - search across accessible sites, collections, bookmarks and appropriate local history.
5. **All sites** - a searchable and browsable directory for the long tail of the Sphere.

This structure must remain useful when the user has hundreds of accessible sites. Large sets should be handled through search, grouping and filtering rather than an ever-growing wall of tiles.

Collections and frequency are presentation concepts only. They must never create, infer or modify an Access Class. Automatic ranking may reorder accessible destinations, but it cannot make a new destination accessible.

Ordinary discovery and ranking should include only currently Whitelisted destinations: the destinations that currently make up the Sphere. An Access Grant or a past Greylist visit must not cause a destination to appear among frequent sites, collections or ordinary suggestions.

The start surface should continue to work when there is little or no history. Bookmarks, collections and the full directory provide stable navigation independent of behavioral inference.

---

## 5. Finding and Navigating the Sphere

Zenith needs a first-class way to navigate a large Sphere without making raw URL entry the visual center of the application.

The primary find experience should search the user's Sphere first. Results may include:

- Accessible sites.
- Collections.
- Bookmarks.
- Previously visited pages where local history is enabled.

The result source should be clear. A search of the user's Sphere must not silently become an unrestricted web search.

Ordinary results must be filtered through current Site Policy. History or bookmark metadata does not make an expired or Greylisted destination part of the user's Sphere.

An accessible search provider may still be used like any other accessible site. Links returned by that provider remain separate navigation requests and receive normal policy evaluation.

Search and organization must work for broad categories such as academic and scientific resources without requiring every individual destination to occupy permanent space on the start surface.

---

## 6. Browser Chrome and the Address Field

Raw address entry can be a useful expert tool for known destinations, but it should not define Zenith's visual hierarchy and does not need to appear before a deliberate direct-navigation flow exists.

The default chrome should prioritize:

- The current page and its identity.
- Back, Forward, Reload and Home.
- A compact way to find destinations in the user's Sphere.
- Page-specific actions when they become necessary.

If full address editing is introduced later, it should use progressive disclosure rather than becoming a permanent central field. Its exact placement and keyboard path should be decided alongside Site Policy and Greylist entry behavior.

Omitting the address field must not ultimately conceal security-relevant site identity. Before Zenith renders permitted external pages, the shell must provide a clear way to inspect the current hostname or origin and reveal the complete address when needed.

The default browser chrome should not contain a permanent "policy locked," "Whitelist" or similar status. Successful policy enforcement is the silent baseline.

---

## 7. Greylist Experience

Greylist access is an exceptional path, not a competing mode of navigation.

Routine surfaces should not promote Greylist exploration, show Greylisted suggestions or place a prominent "browse outside" action beside ordinary destinations. The user is assumed to want exceptional access infrequently.

Greylist access must nevertheless remain deliberately discoverable. Appropriate entry points include:

- A secondary action after the user intentionally requests a Greylisted destination.
- A low-prominence command in an application menu or command surface.
- A clearly documented keyboard-accessible route.

When a Greylisted destination is requested, Zenith should present a calm native boundary surface. It should:

- Identify the requested destination clearly.
- Explain that the destination is not in the user's Sphere.
- Keep returning to the Sphere as the primary action.
- Offer temporary access as a secondary, explicit action.
- Avoid suggesting a durable Policy Change as the convenient solution.
- Never make the interaction feel like an error dialog, punishment or security emergency.

The full password-cooldown-password sequence begins only after the user explicitly chooses the exceptional path.

Low prominence must not become deception. Zenith should not hide an active cooldown, misstate why access is unavailable or make a legitimate deliberate request impossible to complete.

---

## 8. Blacklist and Failure Surfaces

A Blacklisted destination is unavailable and does not offer the Greylist procedure. The interface should state this plainly without presenting a bypass or immediate policy-editing route.

Zenith must distinguish between:

- A Greylisted destination that supports deliberate temporary access.
- A Blacklisted destination that is unavailable.
- A technical navigation failure.
- A policy-unavailable or restricted-recovery state.

These states may share a calm visual language, but they must not be collapsed into one generic "blocked" experience.

Policy-unavailable recovery is exceptional. It should not define the normal start surface or leave a permanent warning in ordinary chrome. Diagnostics may expose precise policy reasons without turning routine browsing into a security console.

---

## 9. Vault Placement and Character

The Vault should be treated as a protected settings destination rather than a primary browser destination.

It should be reached through the application menu or settings structure, not through a prominent start-page tile or permanent toolbar button. Ordinary preferences that do not affect access policy should remain separate from protected Vault controls.

The Vault remains a user-facing interface, so its labels for the Whitelist-backed zone should also use Sphere: for example, "Sites in your Sphere" and "Add to your Sphere." Those actions still create normal Policy Changes that operate on Whitelist membership underneath. An advanced technical detail may explain that mapping when useful, but ordinary Vault controls should not switch back to Whitelist as their primary label.

The Vault should make durable consequences explicit. Pending Policy Changes, waiting periods and confirmation eligibility should be visible when the user intentionally visits the Vault, but they should not occupy ordinary browsing surfaces. Other canonical policy terms remain available where they are necessary to understand the protected operation.

The Vault remains more than an ordinary settings page: access-affecting edits still require authentication, staging, delay and confirmation. Its placement should feel familiar; its behavior must remain protected.

---

## 10. Language and Terminology

Canonical domain terms remain necessary in specifications, Core code, persistence, tests and developer-facing diagnostics. Primary user-facing labels must use Sphere whenever they name the ordinary browsing environment or the Whitelist-backed zone.

Sphere is deliberately scoped experience vocabulary, not a friendly replacement Access Class. Under the interface, being in the Sphere means having current Whitelist membership. Blacklist, Greylist, Access Grant and Policy Change keep their documented meanings.

Examples:

- A normal start surface presents the user's Sphere without labeling individual destinations "Whitelisted" or "Sphere-classified."
- Tab tooltips and accessible help expose site identity without announcing its Access Class.
- A boundary explanation says a destination is not in the user's Sphere before offering the deliberate Greylist path.
- The Vault says "Add to your Sphere" while the underlying domain operation changes Whitelist membership through a Policy Change.
- An Access Grant never expands the Sphere, even when it temporarily permits a Greylisted visit.

Capitalize **Sphere**. Prefer "your Sphere," "in your Sphere" and "add to your Sphere." Do not use "Sphere-listed," "Sphere-classified" or Sphere as a synonym in policy identifiers.

Copy should be calm, direct and non-judgmental. It should describe what is available and what the user can do next rather than congratulate Zenith for denying access.

---

## 11. Visual and Interaction Character

Zenith should feel calm, spacious and content-forward.

- Normal surfaces use neutral visual weight rather than warning colors.
- Browser chrome recedes behind the current task.
- The start surface favors a few useful choices and fast retrieval over exhaustive display.
- Motion, badges and notifications are used sparingly.
- Exceptional-access actions have lower visual prominence than returning to ordinary browsing.
- Policy details appear when they help the user understand a boundary or intentionally manage policy.

Security must not depend on visual subtlety. Native boundary surfaces must remain distinguishable from website content, and all actions must have accessible names, focus behavior and keyboard paths.

### Theme decision

Phase 1 establishes a single default midnight-blue theme because visual hierarchy and emotional character are part of the shell correction, not a finishing layer. The palette should use deep navy rather than pure black, softly layered blue surfaces, warm off-white text, muted blue-gray secondary text and a restrained cool-blue accent. Soft corners, subtle borders and comfortable spacing should make the interface feel cozy without decorative clutter or reduced contrast.

Views should consume semantic color resources rather than hardcoded theme colors. Windows High Contrast is an accessibility override and should replace the fixed palette when active.

A theme picker, light palette, persistence and normal system light/dark synchronization are deliberately deferred until a later usability and product-readiness pass. The native Windows frame remains intact; where Windows supports it, the native DWM caption color follows Zenith's semantic chrome palette without replacing the frame.

---

### Settings checkpoint

General settings includes a privacy/security card with the running WebView2 version, restart advice for engine updates and confirmed browsing-data cleanup. Cleanup explains sign-out, unsaved-work loss and app closure before proceeding; Vault rules, waits and Zenith bookmarks remain intact. The application menu's Website identity command shows the actual normalized origin without exposing URL tokens, distinguishes HTTP from HTTPS and points to Ctrl+L for the complete address. It does not add a permanent policy badge or claim that HTTPS proves site trustworthiness.

The sidebar settings control opens a dedicated, owned native settings window. It uses the shared midnight palette and Windows High Contrast resources, with General, Your Sphere, Temporary access, Vault and About sections. Closing settings returns to the existing browsing surface without unloading or navigating its page.

General settings persist the startup sidebar layout and default page zoom. The page zoom applies to existing and newly created tabs. These presentation preferences are ordinary local data, separate from durable access policy. The Sphere directory is filterable and opens destinations through the normal coordinator. Full theme selection remains deferred.

Temporary access provides initial password setup and a list of pending requests with eligibility and resume actions. The native gate identifies the exact hostname, explains each challenge using current timing rules and preserves the saved wait when closed. It is reached as a secondary action on an intentional Greylist boundary, with returning to the Sphere still primary.

The Vault page separates active rules, a proposed change and its pending confirmation. Users can edit durations with seconds/minutes/hours/days, add or remove multiple services, and propose a password replacement. Known services are selected by friendly name; an expandable custom-site form accepts another hostname and an optional display name. Each new addition defaults to “this service only,” with an explicit option to include subdomains. Additions can be queued and undone; removal uses checkboxes for independent active scopes rather than exposing redundant covered children. A broad parent can be removed while selected services are added in the same reviewed change. Review uses names and plain-language scope, with technical addresses available under “Addresses and scope.” Copy explains that parent and sibling services are never included by a child entry. Pending changes show their saved deadline, countdown, confirmation, edit and cancel actions. Editing explains that it starts the wait again; nothing applies automatically. Five-second development values are visibly identified as testing values. Forgotten-password recovery is not offered.

Settings → Temporary access includes a quiet, explicitly labelled website-address field. It accepts HTTP(S) URLs or bare website addresses such as `example.com/path`, not search queries. Omitted schemes default to HTTPS; `www.` is neither required nor automatically added. It continues to the existing access challenge without loading the site or starting a wait. Core decides eligibility; invalid or ineligible addresses receive inline feedback. Pending requests remain resumable below it. A separate active temporary visits section lists currently authorized exact hostnames and their expiry times, refreshing while the page is open. Expired visits disappear; this is not persistent browsing history. This portal does not add suggestions or promote temporary destinations into ordinary discovery.

## 12. Experience Invariants

An interface change is consistent with Zenith only if all of the following remain true:

- Opening Zenith first presents the user's Sphere, not the inaccessible public Internet.
- A user with hundreds of accessible sites can find any of them without scanning hundreds of tiles.
- Any future direct address entry remains secondary to Sphere navigation.
- Ordinary browsing does not continually expose classification or enforcement state.
- Greylist access is deliberate, secondary and understandable.
- The Vault is reachable but absent from the ordinary browsing path.
- The current site's identity remains inspectable.
- Presentation ranking and organization never modify policy.
- A quieter interface never creates an unrestricted fallback or conceals a meaningful failure.

---

## 13. Current Shell Baseline

The completed Phase 1 shell establishes the first concrete expression of this experience:

- A Sphere-first start surface replaces the previous lock-focused explanation.
- The shell uses an approximately 248 px open sidebar, collapsing to approximately 64 px, for navigation and wayfinding.
- The sidebar contains the single "Find in your Sphere" field, bookmark shortcuts, open tabs and the application menu; the start surface does not duplicate it.
- The main browser surface fills the remaining window. When it is empty, it uses Zenith's ambient background rather than homepage cards or widgets.
- There is no separate permanent address bar in the shell; the Sphere field accepts a typed URL only when the Core policy allows it, and `Ctrl+L` focuses that field.
- There is no separate current-address row. Tab tooltips and accessible help expose the complete address, while `Ctrl+L` places the current address in the Sphere field for deliberate editing. `Ctrl+K` remains Sphere search. Typed website addresses accept omitted HTTPS and do not require `www.`, using the same Core input parser as Temporary access.
- Ordinary chrome contains no permanent classification or policy-warning status.
- The Vault is reached through the application menu rather than a permanent browser control.
- An intentional unavailable navigation opens a calm native boundary with returning to the Sphere as the primary action.
- A fixed midnight visual system is expressed through semantic resources and yields to Windows High Contrast.
- The WebView2 host and every implemented navigation origin remain behind the centralized fail-closed coordinator.

Phase 2 extends this baseline with a filterable directory beneath "Find in your Sphere." Focusing the field reveals all currently Whitelisted starter sites and bookmarks; typing narrows the list, and bookmark stars manage ordinary saved destinations without changing Site Policy. Saved bookmarks immediately become sidebar shortcuts. Removing a bookmark is consistently expressed by de-starring it from the current page or Sphere directory rather than by a separate shortcut-close action. Distinct native boundaries explain Greylist, Blacklist, unsupported-address and policy-unavailable decisions. Native Sphere and boundary surfaces unload the external document they replace, while ordinary inactive tabs are suspended where WebView2 permits it.

Phase 3 adds the deliberate Greylist procedure and settings described above. Temporarily permitted destinations do not enter ordinary discovery or become eligible for bookmarking. Phase 4 adds the protected Vault and live, durable Sphere additions. Collections and history ranking remain later work; their interfaces must extend this baseline without changing its hierarchy or moving policy decisions into App.
