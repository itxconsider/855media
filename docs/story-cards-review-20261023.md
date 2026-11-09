# Story card review: posting and ScreenTime — 2026-10-23

User-authorized visual and Android UI review on the four running devices LD-1, LD-2, LD-4 and LD-6. Windows computer-use screenshots were compared with ATX UI hierarchies. Captures and live navigation were limited to Feed cards, own-story playback, media-picker entry and viewing other stories. No story was submitted; no reply, reaction, follow, reshare or upload action was used. Story views can mark content seen. Original personas were retained; tested routes returned to Feed.

## Captured layouts

| Surface | Observed behavior | Change/coverage |
|---|---|---|
| LD-1 Page Feed: stacked Create a story/Create reel, Your story and suggested stories | Create selector returned null despite a visible Story card | Exact Create a story alias accepted only inside the story tray |
| LD-2 and LD-4 Page Feed: stacked create cards, no own story | Empty-own baseline could not be established | Same alias supports a loaded-tray baseline; missing/hidden/disabled evidence remains unknown |
| LD-6 personal Feed: music tile, Create story, Your story | Existing create label retained; a scrolled tray may hide it | Preserve old route and exclude own/music/create cards from viewing |
| Page own-story viewer: Add new story, Highlights current story and Share | Old reader expected Share as post and returned unknown count | New own-toolbar variant supported with marker, usable own controls and unchanged owner/count/upload checks |
| Personal own-story viewer: Add new story and Share as post | Existing count route supported | Captured regression preserves it |
| Suggested viewer with reply field and three reaction buttons | No story_id, native Story index/count, author avatar, Open story tray, More options | ScreenTime uses a local playback key from unique author + position; never a publication ID |
| Suggested viewer with reaction bar and no reply field | Same native markers, no story_id | Recognized by the same read-only viewer evidence |
| Page and personal Story pickers | Existing registry recognizes both | Captured fixtures and live entry confirmation |

Ten sanitized captured XML fixtures under docs/fixtures/pflow-story preserve hierarchy/control geometry for these representative layouts. They are not a claim to cover every Facebook account/version/language or every editor/media layout.

## Posting changes and safeguards

CreateStoryEntry accepts Create story and Create a story only as unique, usable controls inside the recognized Feed story tray. It does not choose Create reel or the global Create menu. HasEmptyOwnStoryBaseline uses the same supported labels and still rejects an existing own-story marker or loading evidence. The Page own-viewer variant requires usable Add new and Highlight/Share controls plus the native story-position marker. Fresh target identity, owner matching, exact media staging, upload/pending rejection and exact before/after publication count remain in force. A playback key never enters publication verification.

## ScreenTime changes and safeguards

Native card selection uses the explicitly clickable visible story row and the tray scope, rather than an overlapping nonclickable wrapper. Own/create/music cards, foreign/auth/viewer surfaces and unusable controls are excluded. Viewed author keys ignore Unseen→Seen changes. When visible authors are exhausted, up to two acknowledged horizontal swipes per iteration are confined to a recognized Feed tray; ordinary session/cancellation limits remain.

A native viewer can be recognized without story_id only with a valid story-position marker, one usable author avatar, Open story tray and More options controls. Own-story controls and contradictory/foreign layouts do not qualify. This key is local session evidence, not a Facebook identifier. The viewer is observed through bounded loading polls; a view counts only if its playback key is still stable after the configured dwell. Auto-advance to different content remains conservative/uncounted. Exit uses Close story when supported or Android Back; it never touches reaction, message, follow, highlight or share controls. ScreenTime retains the existing personal-account identity policy.

## Live results

- Production CreateStoryEntry → recognized StoryPicker passed on LD-4 (Page) and LD-6 (personal). LD-2 Page picker was also inspected. Pickers were closed without selecting media.
- Page own-story count: two; personal own-story count: one. Both viewers were closed without changing content.
- Updated production ScreenTimeInteractions on LD-6, with a fresh personal Graph check and isolated diagnostic history: one-view run reported Stories=1, Skipped=0; three-view run reported Stories=3, Skipped=0. Both returned to Feed with no replies/reactions/shares. These invoke the real viewing component, not the outer subscription/account/session scheduler.
- Earlier diagnostic observations required ATX startup and settling/polling after horizontal tray movement. Initial premature captures were not treated as supported UI evidence; the final create checks used the production selector.

## Validation

All 325 Story/ScreenTime regressions passed (0 failed/skipped); the final captured-layout run passed 12 cases. Main Debug build passed with zero warnings/errors. Evidence is recorded in artifacts/story-review-final-regressions.log, artifacts/story-review-test-results and artifacts/story-review-build.log. Live reports: artifacts/story-review-live-screentime.log, artifacts/story-review-live-multiple.log, artifacts/story-review-production-create.log and artifacts/story-review-updated-own.log. Raw captures remain under artifacts/story-review-20261023. Full live story publication and unsupported/localized layouts remain pending; no Share was pressed. Packaged 3.1.9 ZIP is unchanged.

This implementation/test set counts as one improvement: release progress is 3/10 toward 3.2.0, including prior credits.
