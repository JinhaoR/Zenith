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
