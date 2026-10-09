# Facebook deep-link investigation — 2026-10-09

## Scope and installed version

Reviewed this worktree's `FacebookAccountDiscoveryService.cs`, `FacebookBloksService.cs`, `LDPlayerService.cs`, `AtxAgentClient.cs`, and `BloksModels.cs`. Probed the installed app on LD-6 using the runtime helpers from `D:\repos\855Media`, after confirming the Android Surfshark VPN connection.

The final package-manager check and freshly pulled APK agree:

- Package: `com.facebook.katana`
- Version: **582.0.0.50.54**
- Version code: **475417106**
- Minimum SDK 28; target SDK 36
- Android package last-update time: `2026-10-09 08:07:00`

An early startup check reported 580. Final live results below are against 582; the reason for the startup discrepancy was not established.

The app began the route probes with a managed Page as its active identity. Each probe cleared/restarted Facebook, captured the hierarchy, submitted the route, waited six seconds, and captured the resulting hierarchy. These checks do not establish compatibility for every account or Facebook build.

## Observed routes

| Route | Helper returned | Result on LD-6 |
| --- | --- | --- |
| `fb://profile/{personal_uid}` | true | Personal profile opened with **Message**; did not switch from the active Page to the personal identity. |
| `fb://page/{delegate_page_id}` | true | Page profile opened with **Edit profile**, **Professional dashboard**, and **Advertise**. This Page was already the active identity. |
| `fb://profile/{page_persona_uid}` | true | Same owned Page profile opened. |
| `fb://accounts_center` | true | Stayed on the feed; no Accounts Center screen. |
| `fb://switch_account` | true | Stayed on the feed; no account switcher sheet. |
| `fb://professional_dashboard` | true | Stayed on the feed; no dashboard or Analytics screen. A feed navigation tab bearing this name is not evidence of successful navigation. |
| `fb://bloks_screen_query` with `com.bloks.www.pages.management` and root-derived Page parameters | true | Stayed on the feed; no requested Page query screen. |
| `fb://bloks_screen_query` with `com.bloks.www.fx.settings.modular.screen` | true | Stayed on the feed; no Accounts Center query screen. |

Hierarchy changes alone cannot prove success: loading feed content also changes the hierarchy.

## Confirmed causes in the linked source

1. **Intent acceptance is treated as screen success.** `LDPlayerService.OpenDeepLinkAsync` (line 563) accepts ATX success or any ADB response without the word `Error`. `AtxAgentClient.OpenDeepLinkAsync` (line 575) submits `am start` without verifying the destination. Facebook's generic `IntentUriHandler` can accept the URI without opening the requested internal screen.
2. **Accounts Center navigation is not verified.** `FacebookBloksService.OpenBloksAccountsCenterAsync` (line 79) tries an alternate route only when submission returns false. The observed unsupported navigation returns true, so this branch does not detect the failure.
3. **Query precedes identity switching.** The root-discovery Page loop queries at `FacebookAccountDiscoveryService.cs:651` and switches at line 665. The lookup consequently runs in the previous identity. Supplying `page_uid`, `page_id`, or `delegate_page_id` is not proof that Facebook changed its authenticated acting identity.
4. **Profile navigation and persona switching are different operations.** The personal-profile probe opened a visitor view while a Page was active. A Page link can show owner controls when that Page is already active; it should not be described as always opening a public view.
5. **The Bloks query contract is unverified.** `BloksModels.cs:182` supplies `com.bloks.www.pages.management`; line 207 constructs `app_id` and `bloks_screen_params` with `server_params`/`client_input_params`. This combination did not resolve on the tested session. The experiment cannot distinguish an obsolete URI contract, invalid app ID/parameters, or account/server gating.
6. **Missing and zero metrics are conflated.** `FacebookBloksService.ExtractBloksPageInfoAsync` rejects an otherwise undated result whose counters are zero. A verified zero should be represented separately from an unavailable field.

## Private Bloks hosts

The current APK manifest marks these activities `android:exported=false`:

- `com.facebook.bloks.facebook.nativeshell.FbBloksNativeShellActivity`
- `com.bloks.foa.screen.genericcontainer.BloksScreenQueryGenericContainerActivity`
- Its transparent variant
- `com.facebook.pages.profileswitch.ui.fullscreen.ProfileAccountSwitcherFullScreenActivity`

A normal ADB launch of `BloksScreenQueryGenericContainerActivity` produced:

```text
Security exception: Permission Denial: starting Intent ... not exported from uid 10064
```

This confirms that the service's ordinary shell invocation cannot launch this private component directly. It does not prove that all in-app Bloks navigation is disabled. The Native Shell example list is also not evidence that a particular production data query is available.

## Recommended implementation

1. Keep root discovery for authenticated user UID, Page persona UID, and delegate Page ID; publish/save these records immediately.
2. Open the currently active own profile and use its account-switcher dropdown. Select the requested personal or Page identity and verify the selection plus exact profile name and owner controls before scraping.
3. Read birthday from personal details and join/creation date from the profile-name information sheet. For Pages, use the Page's own sheet; do not copy the managing account's join date to its Pages.
4. Read Page metrics through Professional dashboard → Analytics after confirming the Page identity.
5. Track URI submission separately from verified destination. On timeout, record URI, expected screen/identity, actual screen, and hierarchy evidence. Do not scrape a feed as though the query opened.
6. Keep the speculative internal query route out of the production path until a valid app ID, parameter contract, and expected destination have been demonstrated on this installed build. Fixed delays cannot repair a route that never opens.

## Repository distinction and evidence

The user runs `D:\repos\855Media\855Media` with `dotnet run`. That repository currently does **not** contain the split `FacebookAccountDiscoveryService.cs`; its implementation is in `LDPlayerService.cs` and related partial files. Earlier production-flow fixes were applied there. The ordering issue identified above is in this chat's linked worktree; changing only this worktree would not change the app launched from D.

Probe artifacts are local and may contain personal UI text:

`C:\Users\itxco\AppData\Local\Temp\855Media-BloksAudit\route-*.json` and `route-*.xml`.

The freshly pulled APK is `facebook-LD6-current.apk` in the same directory. The older `facebook-base.apk` is not the current-build evidence. The route probe did not write account-table storage. No production source changes were made during this investigation.
