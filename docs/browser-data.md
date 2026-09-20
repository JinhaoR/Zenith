# Browser data management

## Current storage model

The first WPF WebView2 calls `EnsureCoreWebView2Async()` without a custom user-data
folder or profile. MainWindow retains that environment and supplies it to later
tabs. Production uses one persistent default profile, with no profile selector or
InPrivate mode. Cookies, local storage and other browser state can survive closing
and restarting Zenith. Tabs share that state subject to Chromium's origin rules.

WebView2's default folder is the executable path plus `.WebView2`, normally
`<application directory>\Zenith.App.exe.WebView2`. Settings → General → Browser
data shows the actual environment folder, profile name and profile path obtained
from the running engine. Environment overrides may change the location. Moving
or launching another executable location can use a different folder: Debug,
published builds and the isolated extension experiment do not necessarily share
sessions. There is no automatic profile migration in this change.

WebView2 manages cookies, HTTP cache, browsing history, DOM storage (including
local storage, IndexedDB and Cache Storage), browser permissions and other profile
metadata. Zenith does not maintain another history or cookie database.
[Microsoft's user-data-folder guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder)
describes the default location and API for retrieving it.

Zenith separately stores Vault policy/authentication/cooldowns, preferences,
bookmarks and filter caches under its application data directories. Browser-data
clearing does not reset those. Session-only Access Grants end when Zenith closes,
as usual; saved pending waits remain.

## Controls and scope

Settings → General → Browser data offers these whole-profile, all-time actions:

| Action | WebView2 data kind | Scope |
| --- | --- | --- |
| Clear browsing data | `AllProfile` | Supported profile browsing data, including the categories below, browser settings and locally saved autofill/password data. |
| Clear cookies | `Cookies` | Cookies only; other site storage remains. |
| Clear cache | `DiskCache` | HTTP disk cache; website Cache Storage remains. |
| Clear browsing history | `BrowsingHistory` | Browser history; downloaded files and site storage remain. |
| Clear all site data | `AllSite` | Cookies and DOM storage, including local storage, IndexedDB, file-system storage, Cache Storage and service workers. History and disk cache remain. |

The installed SDK defines the inclusive scopes; see
[`CoreWebView2BrowsingDataKinds`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2browsingdatakinds)
and Microsoft's [clearing API guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/clear-browsing-data).

Every action describes its effects and requires native confirmation. Cancel does
nothing. Confirmation destroys Zenith's live tab controllers before clearing,
uses a maintenance controller in the same environment/default profile, then closes
Zenith. This prevents open pages immediately recreating cleared state. The
App-layer `IBrowserDataService` / `BrowserDataService` maps actions to the native
profile API and waits for completion, with a 60-second limit. Failures propagate to
Settings; Zenith does not resume browsing or claim successful removal after a
partial failure. No filesystem deletion or new database is used.

Startup service-worker cleanup uses the same API adapter with its existing
30-second deadline. That fixed security cleanup remains separate from user choices.
Navigation guards disable HTTP cache use and service-worker responses as before;
clearing cache can still remove residual data from earlier browsing. Existing
password saving, autofill and permission restrictions remain unchanged.

## Limits

- Clearing cookies alone is not a complete logout guarantee: websites may retain
  authentication state elsewhere. All site data is the broader website-state reset.
- These APIs do not delete the user-data/profile directories, other profiles,
  experiment folders, downloaded files, cloud/server records, backups, or Windows
  credential stores. `AllProfile` does not erase account-scoped remote data. There
  is no forensic secure-erasure guarantee or extension-uninstallation control.
- No per-site, date-range, cookie viewer, history viewer, disk-size estimate or
  profile switch/delete UI is provided. History clearing is supported even though
  Zenith has no history viewer. Restarting closes the current session's Back/Forward
  stack for every action; that alone is not evidence of history-database deletion.
- Moving the default folder to a stable writable application-data location remains
  a separate migration task. This implementation intentionally preserves existing
  profile locations and sign-ins.

## Verification

Native tests seed persistent cookies, local storage and Cache Storage, close the
application, reopen the same profile, invoke each action through MainWindow's
controller shutdown flow, then reopen again. They verify the expected selective
removal/preservation and Settings' real folder display. Unit tests verify all API
scope mappings and failure propagation. History and disk-cache actions complete
through the supported native API; the tests do not inspect Chromium's private
history/cache database formats. The existing complete-clear security test also
verifies that protected Vault data is unchanged.

Manual checks:

1. Open Settings → General and note the displayed profile path. Restart the same
   executable and confirm that a test site's persistent sign-in/preference survives.
2. Choose a clearing action and cancel; browsing should continue unchanged.
3. Clear cache, confirm, then reopen Zenith. Site sign-ins should normally remain;
   page resources may need downloading again.
4. Clear cookies using a disposable test account, reopen, and check its sign-in.
   Other website preferences can remain until Clear all site data is used.
5. Clear all site data, reopen, and check that the test site's local preferences
   and offline data are reset. Finally try Clear browsing data for the broader reset.
6. Confirm that Sphere entries, bookmarks, Vault settings and pending waits remain.
   A different executable/profile must be checked separately.
