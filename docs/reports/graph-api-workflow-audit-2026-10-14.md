# Graph API replacement audit — 14 October 2026

## Finding

The best migration target is the Facebook **Page** workflow. Identity reads, Page information and publication verification can reduce UI navigation. Page publishing can use a separate API backend after its token capabilities are tested. Personal-profile publishing, Screentime, authentication challenges and emulator management retain their native workflow.

This is an audit, not a migration. No production workflow was changed and no content was published or deleted. The existing live test proved only `/me?fields=id,name`: five personal identities and three Page identities returned successfully; two logged-out instances were excluded.

“Candidate” below means an API operation exists in Meta's SDK, but the current LD tokens have not been proven to authorize that operation. An endpoint's presence does not establish token permissions, app approval, task access or compatibility with the desired media/layout features.

## Current workflow mapping

| Current workflow | Existing implementation | API replacement assessment |
|---|---|---|
| Personal UID/name verification | `PFlowNativeAccountNavigator.ObserveAsync`, own-profile/settings/clipboard navigation | Tested API fast-path candidate. Require the returned personal ID to equal the mapped owner UID. Preserve UI actor checks when continuing a UI workflow. |
| Page identity verification | Persona switcher, Page profile/settings and live target checks | Tested Page-token identity candidate. Compare Graph ID with the expected canonical Page ID, not the underlying personal UID. API-only posting does not require switching the emulator's visible actor. |
| Managed Page discovery | `ExtractFacebookAccountsFromRootShellAsync`, `FetchMappedAccountsAsync`, Page lookups | Candidate: authorized user `/me/accounts` results can supplement/replace cached enumeration. Requires a user token; a current Page token is not interchangeable. Paginate and preserve owner mapping. |
| Single/multiple Page information fetch | `FetchSinglePageAsync`, `RunPageUiLookupsAsync`, `FetchPageInfoFromDeviceAsync` | Candidate: Page fields and supported insights can replace some profile/dashboard reads. Missing fields remain unknown rather than zero. |
| Birthdate, email, location, account/Page creation date | `PFlowProfileDetailsReader`, `PFlowProfileDates`, Accounts Center/transparency navigation | Partial candidate only for permitted fields. Do not equate a Page's business founding date with Facebook Page creation, or a first post's date with account creation. Keep UI/unknown fallback for unavailable data. |
| Page photo/text posts | `PFlowRunner` → `PFlowAutoPostService.PublishAsync`, Android share/composer flow | API backend candidate using Page photo/feed publishing. Preserve caption/media intent and history controls. |
| Page video/Reel posts | Same runner, media transfer, upload/composer/notification checks | API backend candidate using upload phases and Page video/Reel publishing. Confirm target and processing/publication state. |
| Page Stories | `PFlowAutoPostService.Story`, story layout and share-destination navigation | API backend candidate. Native Story styling, stickers, music and share-destination settings need feature-by-feature comparison. |
| Personal-profile posts/Stories and configured group destinations | Same composer paths and routing | Keep native route. The Page API backend must not be used for these targets; no equivalent replacement was established by this audit. |
| Publication confirmation | Fresh UI evidence, toasts, Android notifications; `PFlowConfirmation` | Strong candidate for API-created Page content: store returned IDs, then read exact content/publication status. A successful upload is not automatically a published Reel. |
| Feed/Reels viewing, dwell, gestures, reactions, random comments, stories, Marketplace, notifications | `ScreenTimeService`, `ScreenTimeInteractions`, `PFlowFeedEngagementService` | Keep native. Page content endpoints are not the logged-in user's News Feed/Reels playback environment. API calls do not reproduce watch time, pauses, swipes or the current UI context. |
| Login, OTP, checkpoint, account rotation, restore personal actor | `PFlowAutomaticSessionService`, native login/onboarding/OTP and switcher services | Keep native. Valid token evidence can shorten verification; it does not perform or prove UI login, solve checkpoints, or switch the Facebook app's active actor. |
| LD boot/shutdown, network/VPN, recent-app clearing and routing | `LDPlayerService`, `PFlowSessionPreparation`, network/session helpers | Keep device infrastructure. Page API jobs can avoid booting an LD only when a separately maintained valid token is available. |
| Local media availability/counts, queue, duplicate detection, dry run, daily limits | `AutoPostViewModel.MediaCounts`, `PFlowMediaAvailability`, `PFlowTaskRouting`, `PFlowHistoryStore` | Keep local orchestration. Graph content counts would be a separate feature, not a substitute for files available to upload. |
| Local backup/source deletion/remote emulator file cleanup | `PFlowRunner.Files`, source fingerprint and cleanup flags | Keep local. These operations concern media files, not Facebook posts. |
| Facebook downloading/browser detection | `FacebookDownloaderViewModel`, `FacebookPhotoDownloader`, downloader services | Partial candidate for explicitly authorized Page-owned content. No replacement established for arbitrary Facebook URLs, the browser's visible content, personal/group media, or generic download extraction. |
| YouTube/TikTok/DramaBox downloading, dubbing, translation, upscale, licensing/settings | Corresponding ViewModels/services | Outside Facebook Graph API scope; keep existing services. |

## Page deletion is a new feature

The current `DeleteSelectedPagesAsync` removes rows from the app's saved target table. It does not delete Facebook Pages or their posts. Likewise, `IsDeleteReelFile` concerns local media cleanup.

A Page-post deletion tool would be a new workflow: list supported content with pagination, export/review Page and post IDs, then delete confirmed targets individually. It must not be wired to either existing local-removal command. Bulk requests still represent individual operations and need partial-failure reporting.

## Proposed integration order

1. **Read-only identity adapter:** fresh token read, `/me`, explicit personal-versus-Page result, canonical ID matching, and the existing UI fallback. Do not set the existing own-profile proof flags merely because a token query succeeded.
2. **Read-only capability and data checks:** test permissions, managed Pages, selected Page fields, post reads and supported metrics with the current tokens. Keep capability results per token/actor and avoid overwriting known data when a field is unavailable.
3. **Publication confirmation adapter:** retain returned content IDs and use exact reads, including processing state. Existing UI publications without an attributable ID remain subject to current review rules.
4. **Separate Page API publishing backend:** choose it only for proven Page targets and supported media features. Keep UI publishing for other targets and unsupported features.
5. **Separate, reviewable Page-post deletion workflow**, if requested.

## Architecture constraints

- Keep the root personal owner UID, selected persona UID and canonical Graph Page ID distinct. Names are consistency checks, not unique identifiers.
- A valid Page token proves the token's Page identity. It is not a personal-owner token and does not independently establish every administrative permission or the currently visible emulator actor.
- The Windows app cannot obtain the Facebook application's token by calling the Android SDK's `getCurrentAccessToken`. The current experiment reads the assigned LD's session; a future supported OAuth connection would be a separate token source.
- Root association metadata may be cached. Do not treat a cached owner/Page relationship as a fresh permission check.
- Preserve the source hash, queue reservation, durable submission boundary, returned IDs, unknown-state handling, cancellation, quotas, backups and audit history in `PFlowRunner`/`PFlowHistoryStore`.
- Do not automatically fall back from an uncertain API write to UI posting: that can publish twice. Resolve the first attempt's outcome before another backend submits.
- Tokens remain in memory or protected storage; requests use Authorization headers and logs omit secrets. Capability failures and expiration require explicit handling, not success inferred from `/me` alone.

## Code reviewed

- [Account rotation and per-target workflow](/D:/repos/855Media/855Media/ViewModels/Components/AutoPostViewModel.AccountRotation.cs)
- [Mapped account fetching](/D:/repos/855Media/855Media/ViewModels/Components/AutoPostViewModel.AccountFetch.cs)
- [Single Page fetching](/D:/repos/855Media/855Media/ViewModels/Components/AutoPostViewModel.SinglePageFetch.cs)
- [Native account verification](/D:/repos/855Media/855Media.Core/LDPlayer/PFlowNativeAccountNavigator.cs)
- [Publisher](/D:/repos/855Media/855Media.Core/LDPlayer/PFlowAutoPostService.PFlow.cs)
- [Story publisher](/D:/repos/855Media/855Media.Core/LDPlayer/PFlowAutoPostService.Story.cs)
- [Runner and durable job history](/D:/repos/855Media/855Media.Core/LDPlayer/PFlowRunner.cs)
- [Local file cleanup](/D:/repos/855Media/855Media.Core/LDPlayer/PFlowRunner.Files.cs)
- [Local media counts](/D:/repos/855Media/855Media/ViewModels/Components/AutoPostViewModel.MediaCounts.cs)
- [Screentime](/D:/repos/855Media/855Media.Core/LDPlayer/ScreenTimeService.cs)
- [Facebook downloader](/D:/repos/855Media/855Media/ViewModels/Components/FacebookDownloaderViewModel.cs)

## Evidence and sources

The [live token comparison](/D:/repos/855Media/docs/reports/account-token-live-2026-10-14.md) proves the identity operation with current sessions. Other API capabilities are candidates based on Meta's published SDK interfaces, not tested grants:

- [Meta Page SDK](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/page.py): Page fields, publishing, media, Stories, Reels and insights interfaces.
- [Meta User SDK](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/user.py): account listing and permission interfaces.
- [Meta PagePost SDK](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/pagepost.py): exact post reads, publication fields and deletion.
- [Meta video SDK](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/advideo.py): video fields and upload/status interfaces.

Meta's documentation pages returned rate-limit/access errors during this audit. The official SDK source was used to corroborate endpoint existence. Detailed grants, feature parity and eligibility still need capability tests against the intended API version; generic SDK arguments are not evidence that every field or operation is supported for our tokens.
