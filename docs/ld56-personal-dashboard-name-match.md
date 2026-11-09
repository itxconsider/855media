# LD-56 personal profile rejected despite matching name

The failure at approximately 02:09 on 2026-10-25 was not a different account name. The saved personal account and current Facebook own-profile UI both show Marc Aurelius. The trailing nonbreaking space in one UI label is already handled by complete whitespace-insensitive name comparison.

The captured professional personal-profile layout shows Dashboard and Add to story, with usable Edit cover photo and Edit profile picture controls. It has no separate Edit profile button. `PFlowNativeAccountNavigator.OwnProfile` already recognized the two photo-edit controls, but `PFlowSessionProfileUi.HasOwnControls` additionally required an exact Edit profile label for a personal target. That second guard rejected the otherwise matching profile, producing the misleading name-mismatch error.

The shared name verifier now accepts either the separate Edit profile control or both visible photo-edit labels, while retaining the existing native own-profile control/bounds validation. Dashboard by itself does not establish a personal account. Complete name, visibility/enabled state, own-profile controls, authentication/input/foreign-screen guards and Page-versus-personal exclusions remain required. The general recovery error now says the name **or supported own-profile controls** could not be confirmed.

Validation:

- Read-only current LD-56 UI capture: `artifacts/ld56-name-mismatch-read/current.xml`. No Graph call, actor switch or app restart was made for this diagnosis.
- Sanitized captured fixture: `docs/fixtures/pflow-screentime/ld56-personal-dashboard-captured.xml`.
- Nine new cases passed: the captured layout plus missing/disabled/hidden edit controls, wrong name, foreign app, input and Page evidence. `personal-dashboard.trx`.
- All 249 focused layout/name/account/session/continuation/publication checks passed with zero failures/skips. `artifacts/ld56-name-mismatch-read/focused.trx`.
- Core and test compilation passed. The main desktop build is deferred at the user's explicit request to keep 855Media running; its executable remains unchanged. No live publication or package was produced.

This is one validated source fix, counted once toward the next release: 15/10. Main-output validation remains pending when the user closes the app.
