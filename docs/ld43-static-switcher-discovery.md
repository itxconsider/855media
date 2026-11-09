# LD14-43 notification rows and retained own-profile scroll

2026-11-05, Asia/Bangkok.

The latest desktop Fetch at18:05:35 stopped at18:06:08 with an unavailable/ambiguous complete actor list. Live capture showed the selected Abigail Ingram row, an Abigail row with3 notifications, Create Facebook Page, a separate Instagram account and Accounts Center. The fixed-list structural matcher compared the Abigail child label to the full parent description, including its notification suffix. It therefore returned no rows even though the whole Facebook list was visible.

The static-footer branch now uses the existing NativeRowName notification normalization, matching the scrollable and compact branches. Matching names remains exact; child-label mismatch, disabled/duplicate rows, footer/selection and Instagram exclusion guards remain. The raw captured scan returns both Facebook names as Unknown types and does not infer access, IDs or actor type from the notification label. Six new captured cases cover singular/plural/count changes and negative controls.

The first live type-discovery retry progressed past the empty-list failure, then exposed the selected personal profile reopening deep in historical posts. Navigation previously scrolled toward the header only when At a glance/See all about info was visible, and could fail to restore from that retained position. The new route permits at most12 upward-content reveals after opening the exact own Menu profile row, with the expected name and selected usable Profile tab establishing continuity. Subsequent uninterrupted safe selected-Profile frames may temporarily omit the scrolled-off name; final own-profile recognition still requires the caller's expected actor/own controls. Foreign/input/authentication/pending/switcher surfaces cannot authorize reveals. Existing unrelated navigation and LD9/LD14 Recents flows are unchanged.

Seven new navigation cases cover6/12-swipe success, the12-swipe cap, wrong name/foreign/unselected rejection and intermediate nameless frames. The latter was exposed by the first bounded live recovery attempt: the name disappears between the retained posts and header. The final live retry reached the personal settings control at18:22:20 instead of spending minutes in a Menu fallback.

## Validation

All158 focused tests passed across own-profile navigation, static switchers, badge-free Fetch, type inference and actual native Fetch adapters. Main Debug3.2.4 and diagnostic build succeeded with zero warnings/errors after fresh desktop-process absence checks. Main, tested and final live-tool Core assemblies match. Captures: docs/fixtures/pflow-layouts/ld14-43-switcher-notifications.xml and ld14-43-own-profile-scrolled.xml. Test evidence: artifacts/ld43-fetch-discovery/tests/completed.trx.

Initial type-discovery attempts and their failures are retained in live-fixed/live-final. The final retry is in live-completed. These diagnostics leave production account/Page/history stores read-only, use API-off native UI, and do not publish. Full UID/creation-date/analytics Fetch and desktop persistence are separate from this actor-discovery check. No release package or production database repair was performed.

The final live production adapter run completed at18:25:05 with Abigail Ingram/Profile selected and Abigail/Page unselected. Complete typed discovery passed, Graph0; keyboard restoration passed. The original personal actor was restored by the discovery routine. Main/test/live Core SHA256:11920D93D60192B5A7CD0CE720E4271E6499D4AF16C61C1C55126B3169E27AAB. No actor type or UID was synthesized in production storage. The twelve-swipe extension grants navigation only; it does not replace final own-profile/type proof.
