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

No explicit capability grants are implemented yet. Core denies WebView2 permission requests (including unknown permission kinds), downloads and external-application launches independently of navigation access. App cancels downloads before saving and suppresses the default download UI; permission denials are handled without saving a renderer-owned permission choice to the profile. Denied requests from the active tab receive a native notice. All initialized tabs attach these guards before being marked ready and detach them on disposal.

Before a tab is ready, previously stored non-default renderer permission settings are reset so old grants cannot bypass the default-deny handler. Device client certificates and browser-level HTTP authentication are denied; ordinary website form login remains subject to navigation policy. Host objects, developer tools, default context menus and default JavaScript dialogs are disabled. The renderer cannot invoke privileged host services; its optional cosmetic message bridge is bounded and has no policy authority.

These hooks are not complete enforcement of every category above. File-picker access, frame-specific coverage, fullscreen, background activity and other paths still require investigation. Configurable permission grants remain Phase 6 work. See ADR 0014 for the account-security checkpoint.
