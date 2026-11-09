# LD9 and LD14 live scheduling test — November7,2026

User initially requested dry tests, then explicitly authorized sharing and chose two hours from now. The frozen target was20:25 Asia/Bangkok on November7 (13:25UTC). Two generated five-second clips labelled `855Media scheduling test / Future publication test` were submitted once each. These are real public scheduled Facebook Reels, not dry-run-only drafts. Neither was immediately published or deleted. Future publication at the scheduled time was not watched or certified.

| Installation/index | Active Page | Android timezone | Verified saved date/time |
|---|---|---|---|
| LDPlayer9 /20 | Caprani Plumbing+ | America/Denver | November7,2026,6:25AM MST |
| LDPlayer14 /20 | TAP Plumbing And Heating | America/New_York | November7,2026,8:25AM EST |

Both generated thumbnails appeared under Creator Dashboard → Content → Library → Filters → Post status Scheduled. Opening each test item, then Reschedule, read the saved date/time above. Back exited the editor without saving changes. The library displayed5:25AM for both; this is consistent with a separate Pacific display timezone, but its timezone was not independently proven. The stored editor was therefore used to corroborate the intended Android-local schedule. No Publish now/Share now button was tapped. Submission journals disallow automatic retries.

## Procedure and evidence

- Checked installation inventory, pending history and ongoing-upload notifications, then examined fresh UI frames.
- Shared Pictures paths exposed the generated clip, but LD9's picker did not index it initially. Pushed the same generated file into native Movies, sent a media-scan broadcast and reopened the picker. LD14 also had the shared-folder copy; only one copy was selected and one submission made.
- Native options, calendar, clock, keyboard-mode hour/minute entry, AM selection, Done and final Schedule were exercised on both installations. Today was already selected; month/year navigation, alternate dates, AM/PM changes and24-hour mode were not live-tested.
- Date, clock and keyboard frames omit the background Scheduling options hierarchy. Their current registry classification is Unknown. The options screens classify correctly; native selected RadioButton checked evidence is present. LD9's scrolled final settings classify correctly. LD14's final settings are Unknown while the empty caption AutoCompleteTextView remains visible. Native default hour is approximately two hours ahead; do not rely on it instead of setting/verifying the requested time.
- LD9 settings/media frames have passive disabled ancestors: the diagnostic's conservative semantic tap guard rejected some controls although direct window interaction worked. Do not globally ignore disabled ancestors to fix this.
- Content library was falsely recognized as PhotoComposer by the existing registry in these captures; this must not become publication authority. A separate scheduled-list/detail/reschedule registry and proof adapter is needed.

Sixteen redacted device hierarchy fixtures are in `docs/fixtures/pflow-scheduling`. Screenshots, generated media, test source and submission journals are in `artifacts/schedule-live`. No raw cookies/tokens or private session extraction was used. Service/ADB operations and direct Computer Use were used for testing; no new production scheduling executor was enabled.

## Same-index installation conflict

Both running installations used index20. Listener5595 switched between VM processes;2222 was also present but did not provide a working independent ADB route. Ownership guards correctly rejected mismatched commands. Tests ran sequentially after temporarily closing the other idle instance, verifying the listener PID against the selected installation's VM before direct ADB operations. Both emulator windows were restored afterward. No routing/port settings, timezone, VPN, keyboard or LD9/LD14 Recents behavior was changed. The concurrent same-index transport conflict remains unresolved and should be addressed separately.

## Result and implementation implications

Manual native scheduling and persisted scheduled-item verification passed on both versions for these two Pages. Registry-only groundwork is incomplete for actual native picker frames, visible-caption scheduled composers and scheduled-content management. Before automation: add invocation-bound native dialog recognition, scoped passive-wrapper handling, explicit schedule-summary parsing/timezone evidence, stable target/media/schedule proof, scheduled acceptance/history outcome and strict retry protection. This validation adds no improvement credit or release package.
