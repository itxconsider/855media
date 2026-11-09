# LD-42 discovery switcher recovery

The November 2, 23:54 LD14 Fetch failure occurred while discovering actor types:
ProEra Plumbing had been inspected, but selecting Angus Schofield failed with
`switcher-open-unavailable`. The diagnostic captured Facebook Home rather than
a readable switcher. This preceded UID/API lookup and progressive collection.

Initial Fetch discovery, discovery row selection and post-selection verification
now have one bounded alternative after normal switcher opening fails:

1. Read a fresh safe hierarchy under the existing Fetch installation/process/
   foreground guard. An already-readable sheet can continue directly.
2. From recognized Home, locate one unique enabled visible Menu control with
   fresh bounds. Tap it once. If recovery starts in an existing Menu, request
   Home once and re-enter Menu, rather than immediately repeating its dropdown.
3. Wait through empty/loading/switching animation frames without further taps.
4. On recognized Menu, tap the freshly resolved native switcher once and wait
   for an actual sheet. Recheck the Fetch guard during that wait.
5. Continue the existing complete-list, unique-name and selected-row checks.
   Acknowledging navigation does not accept an actor or invent its type.

Navigation allows at most 40 observations/30 seconds; the final switcher response
uses the existing bounded 20-second waiter. Disabled ancestors, missing/duplicate
Menu controls, foreign/input/auth surfaces, cancellation, changed worker/process
and unchanged controls stop safely. No app restart, Graph request, login or
guessed target selection is part of recovery.

Validation: 129 focused Core checks passed, including 14 new captured/fallback/
selection/animation/negative-guard cases. The captured LD42 Home opens Menu and
the switcher with two exact navigation taps in the fixture. A normal failed
profile-dropdown scenario recovers through freshly entered Menu, and the actual
Fetch selection method re-enumerates the recovered sheet before accepting its
selected actor. Existing native Fetch/device/badge-free/discovery/owner recovery
checks passed. Main Debug rebuilt with zero warnings/errors after verifying the
app was absent. No live device action, production data edit, publication or
package. Full live LD42 Fetch and UID/API resolution remain unverified.

Updated executable: `855Media/bin/Debug/net10.0/855Media.exe`.
One related improvement credit: 31/10 → 32/10 toward 3.2.5.
