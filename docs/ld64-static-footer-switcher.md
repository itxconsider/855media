# LD-64 fixed-list switcher support

Completed 2026-10-24; one improvement credit toward 3.2.0.

LD-64 exposed a recognized `switcher.footer.v1` sheet containing an explicitly selected personal row and Mattea Page+, followed by Create Facebook Page, an Instagram section and Go to Accounts Center. Unlike existing supported sheets, it had neither a scrollable RecyclerView nor Page badges. Native row discovery rejected it even though the exact target was visible.

`FindNativeSwitcherRowsCore` now accepts this additional fixed-list form only when it has at least two usable Facebook rows, exactly one explicitly selected row, matching visible child names, a dismiss control above the rows, and both usable footer actions around the Instagram boundary. Existing exact-name/ambiguity/visible-row controls and Instagram exclusion remain in force. This changes row eligibility within the existing registry variant; it does not infer account type/UID from a row or remove destination Graph/session/own-profile checks.

## Validation

- Sanitized captured XML: `docs/fixtures/pflow-screentime/ld64-switcher-static-footer-captured.xml`.
- Ten new checks: exact Page/personal selection, partial-name rejection, absent/conflicting selection, disabled/hidden target, mismatched child name, disabled footer actions, foreign package, matching Instagram name exclusion and duplicate Facebook-name rejection. Two positive cases reproduced the old failure before implementation; all ten pass afterward.
- All 172 focused static/untyped switcher, Page discovery/name and navigation regression checks passed; no failures or skips. See `artifacts/ld64-footer-tests.log`.
- Main Debug desktop build version 3.1.9 passed with zero warnings/errors after the user closed the app. See `artifacts/ld64-footer-main-build.log`.
- Live production switch to Mattea Page+ passed in **14.0 seconds**, including fresh destination Graph verification and Graph-bound own-profile UI confirmation. Two Graph reads through the successful destination check, including initial bootstrap. See `artifacts/ld64-page-switch-fixed-live/results.json`, screenshots/redacted XML and `artifacts/ld64-switch-fixed-live.log`.

The first updated live invocation stopped before navigation because Facebook was closed (launcher visible; no initial process/session proof). The diagnostic gained an explicit `--launch` option and separate `--output` path; the subsequent run opened Facebook normally and passed the Page transition. No production guard was bypassed.

## Restoration limitation

The return switch selected the original personal actor and passed its destination Graph identity check, but its stricter own-profile UI confirmation failed. The captured profile shows the correct personal name, Dashboard, Add to story and profile-picture editing, without the expected Edit profile control. This is a separate personal-profile layout limitation; it remains rejected by production verification and is not claimed fixed here. The device was left on that original personal profile. No post was submitted, no account/source mappings edited, and no app/emulator data cleared.

No package produced; existing ZIPs do not include this source fix. Counter advances from 8/10 to 9/10.
