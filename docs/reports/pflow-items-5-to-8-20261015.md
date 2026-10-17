# Remaining interaction and routing work, items 5–8

Comment handling now consults registered comment-sheet and published-comment components in addition to existing post association and author/text checks. Empty/typed/published XML from LD-21 remains covered. Missing post IDs still cause automatic comments to skip; this change does not replace post evidence with layout evidence.

Five interaction component templates were added to the bundled JSON and compiled Registry: post action bar, applied reaction, reaction picker, comment sheet, and published comment. Components are matched explicitly, so a post action bar does not make whole-Feed classification ambiguous. Existing user JSON retains omitted new defaults. ScreenTime uses the components for scoped action eligibility, reaction choice and applied-state recognition, and comment confirmation. JSON cannot introduce arbitrary public-action control aliases.

Adjacent capture work is reduced by passing the current invocation's fresh authentication capture to the posting target-session verifier. No pre-navigation capture is needed when the first job must open its profile anyway. The publisher and final Share gates still reread the UI after asynchronous work.

An initial account Graph read can be handed to the immediately following posting verifier once. The handoff is memory-only, expires after 15 seconds, and requires exact binding, unchanged Facebook process and an identical captured hierarchy hash. Inputs, invalid captures/process IDs, account preparation, persona changes, ScreenTime, and batch reset prevent reuse. ResolveAsync still compares owner, target, and delegation, and reads again after persona selection. Changes or missing proof fall back to a fresh Graph/UI lookup.

The name-only SwitchToPersonaAsync overload now uses the same native route planner as mapped targets. The duplicate legacy switching procedure was removed. Existing public navigation helpers remain for compatibility. As before, switching confirms navigation; UID verification remains the caller's responsibility.

Regression tests cover captured component controls, exact comment author/body, disabled JSON templates, protected interaction aliases, unchanged Feed classification, one-use/expired/changed startup evidence, capture counts, and the name-only entry route. No live public actions or live emulator navigation were performed for this follow-up.

Validation: all 1,617 LDPlayer tests passed. The desktop app built with zero warnings and zero errors. The previous name-only direct-call fixture was updated to include own-profile controls; a matching name alone no longer satisfies the unified navigation route.
