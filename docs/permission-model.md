# Permission Model

## 1. Separation from Site Access

Site access and website capabilities are separate decisions.

Whitelist membership or an Access Grant permits navigation only. It does not automatically grant camera, microphone, location, notification, clipboard, download or other capabilities.

## 2. Default Behavior

Sensitive website capabilities are denied unless Zenith policy explicitly permits them.

Permissions must be:

- Associated with a clearly identified site.
- Limited to a defined capability and scope.
- Explainable to the user.
- Revocable through the Vault.
- Enforced independently of website prompts.

A Blacklisted site receives no permissions. A Greylisted site's Access Grant does not imply additional permissions.

## 3. Capability Categories

The permission system must account for at least:

- Camera and microphone.
- Location.
- Notifications.
- Clipboard read and write.
- Downloads and local-file access.
- Popups and new windows.
- Fullscreen.
- External application and custom-scheme launches.
- Persistent storage and background activity where WebView2 exposes control.

## 4. Durable and Temporary Permission

A durable permission change is a Policy Change and follows the Vault waiting and confirmation process.

Whether Zenith will support temporary, session-scoped permission grants is unresolved. Such a mechanism must not weaken site classification or create an alternative route around the Vault.

## 5. Failure Behavior

Unknown capability requests, unsupported states and permission-service failures are denied. Website content cannot directly read or modify Vault policy.

## 6. Current Hardening Checkpoint

The application host runs as a standard Windows user. Its explicit manifest does
not request elevation; startup refuses SYSTEM, an enabled administrator token or
an unreadable identity before creating WebView2 or opening protected state. This
is deployment safety, not a Vault-editable permission. Reopen normally if Windows
launched Zenith as administrator. See `microsoft-security-baseline.md` for the
secure-hosting review and verification scope.

Shared/service-worker network activity has no capability grant. Core denies those native request sources, including unknown future source values, without borrowing a tab's URL or Referer. Dedicated workers remain document-scoped and receive transport and resource filtering. Before any tab becomes ready, the supported profile `ServiceWorkers` cleanup terminates and unregisters old service workers once per session; failure closes browsing. This is targeted removal, not cookie/local-storage/password cleanup. New intercepted service-worker script requests are denied. Shared workers can still execute local code, but their intercepted network requests are denied. Offline/background website features may therefore be unavailable. There is no Vault override at this checkpoint.

No explicit capability grants are implemented yet. Core denies WebView2 permission requests (including unknown permission kinds), downloads and external-application launches independently of navigation access. App cancels downloads before saving and suppresses the default download UI; permission denials are handled without saving a renderer-owned permission choice to the profile. Denied requests from the active tab receive a native notice. All initialized tabs attach these guards before being marked ready and detach them on disposal.

Before a tab is ready, previously stored non-default renderer permission settings are reset so old grants cannot bypass the default-deny handler. Device client certificates and browser-level HTTP authentication are denied; ordinary website form login remains subject to navigation policy. Host objects, developer tools, default context menus and default JavaScript dialogs are disabled. The renderer cannot invoke privileged host services; its optional cosmetic message bridge is bounded and has no policy authority.

HTML file-input choosers use Core's fixed file-selection decision in the root target and recursively auto-attached iframe targets. Before an OOPIF target resumes, the App adapter configures its CDP chooser interception and recursive attachment. New targets must be paused for setup; initialization or runtime protection failure leaves no unrestricted fallback and disposes the browser controllers. Core currently denies selection, so no upload permission or file is returned. External drag-and-drop remains disabled on initial and later WPF controllers. Native tests cover top-level, ordinary/nested and verified out-of-process frames, including allowed adapter positive controls without introducing production grants. See ADR 0018 and the [F06 chooser validation](f06-file-chooser-validation.md).

These hooks are not complete enforcement of every category above. The focused validation below resolves tested File System Access/persisted-handle cases; the reproduced OOPIF HTML picker gap is now locally fixed and screen-capture hardening remains separate. Fullscreen, background activity and other untested paths remain outside that validation. Configurable permission grants remain Phase 6 work. See ADRs 0014 and 0015 for the original account-security checkpoints. Disabling HTML file selection also prevents ordinary attachment uploads until an explicit capability flow is designed.

## 7. Focused Account-Boundary Validation (2026-09-13)

[The F02/F06 investigation](account-boundary-validation.md) confirmed an OOPIF HTML
picker-policy gap: the root-target interception does not prevent that frame from
opening a native chooser. No file was selected or silently exposed. The
[2026-09-14 correction](f06-file-chooser-validation.md) closes that reproduced gap
and is locally regression-tested; it adds no new permission flow.
ScreenCaptureStarting remains uncancelled by
production, retaining the browser's native consent boundary rather than enforcing
blanket denial. The test deliberately stopped before the screen chooser.

The tested top-level File System Access pickers were cancelled; cross-origin OOPIF
FSA pickers were refused by Chromium. Seeded persisted file handles could not read
after restart, old FileReadWrite grants were reset, and renewal was denied.
Focused root/OOPIF clipboard and synthetic camera/microphone requests were denied.
These results narrow the earlier investigation gaps without changing policy.
