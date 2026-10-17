# Registry component consolidation — 2026-10-15

The existing C# Registry now exposes 36 versioned definitions across all 18 currently supported screen categories. The generated catalog is artifacts/registry-migration-parts/registry-catalog.json. This counts supported definitions, not live-screen accuracy or all possible Facebook variants.

Menu active header selection, name normalization, section expansion aliases, profile-link Copy visibility and Android Recents selection moved into FacebookLayoutRegistry.Components.cs. Existing callers delegate to these Registry definitions. Added settings overview/copy variants, three semantic Menu variants, a named Page-tools compatibility variant, explicit Recents/chooser entries, and supported authentication/onboarding entries. Existing switcher, own-profile, Dashboard, Analytics, Feed/Reels, Reel/photo composer and Story definitions remain available.

Controls report unavailable or ambiguous evidence. Menu context requires the active header and rejects shortcuts/wrong actors; expansion controls require explicit section context. Settings remain recognizable while Copy is absent or clipped, but Copy stays unavailable until usable. Android's observed non-clickable Clear all exception remains limited to the trusted System UI layout. Facebook foreign-app guards remain active; Recents and chooser have their own guarded package rules.

Specialized parsers provide structural predicates under the Registry. Clipboard operations, UID resolution, task-list emptiness, retries/delays, state machines, publication evidence and file operations remain execution/verification code, not layout definitions. No AI learning, JSON hot loading, source deletion policy changes, account-map changes or live posting were introduced.

Validation:
- 216 focused Registry, Menu/profile navigation, native account, Recents, chooser, switcher and alias tests pass.
- Broad LDPlayer run: 1,473 passed of 1,474; SharedFolderCountsArePerPageAndDeduplicateHistory failed once at its file-replacement count assertion and passed when rerun alone. File-availability implementation was not changed in this task. Treat that result as intermittent, not a clean broad-suite pass.
- Updated app build: zero warnings/errors at C:/Users/itxco/AppData/Local/Temp/855Media-registry-components-build/855Media.exe.
- Added Registry component fixtures/tests for context, duplicate headers, wrong targets, uppercase/clipped Copy controls, settings before Copy is exposed, foreign chooser labels, authentication and both captured Recents layouts.
- Validation was offline with saved/synthetic layouts; live Page fetch completion was not rerun.

The current running app needs restarting into the rebuilt executable to load the new Registry code.
