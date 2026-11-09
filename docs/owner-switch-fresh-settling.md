# Fresh owner-switch recovery and settling

2026-11-02, one improvement toward 3.2.5.

Strengthens the owner recovery used by saved-account Fetch, single-Page Fetch
and name-based account preparation. The prior Menu shortcut routes waited only
500ms before checking the result, and the newly added Page-to-Menu route could
use shortcut bounds observed before an asynchronous idle check completed.

- After the initial idle check, revalidate the fresh owned Page against the
  current account's unique, non-pending saved Page mapping before opening Menu.
- Share one shortcut-selection routine across the Page-to-Menu route, an already
  open Menu and the recovered Feed-to-Menu route. After an idle check, reread the
  guarded hierarchy and resolve the exact unique usable owner shortcut again.
  Disabled ancestors, duplicate/wrong names, foreign surfaces and missing controls
  cannot authorize the old coordinates.
- Tap the fresh shortcut once, then wait up to 60 observations at 500ms intervals
  for two consecutive matching personal-owner headers. Switcher sheets and
  switching screens reset that streak. A transient owner frame does not complete
  recovery. The existing account observer still performs its final independent
  name/type verification.
- If the owner is already active after an idle check, verify two fresh matching
  frames without another tap. Cancellation and worker/foreground guard failures
  stop the operation. Unavailable controls remain a reported failure rather than
  authorizing guessed navigation, another login or Graph lookup.

Validation: 177 focused Core checks passed, including nine new cases covering
stale/disabled/wrong/foreign/duplicate shortcuts, transient versus delayed stable
owner headers, changed Page membership after idle, an already-active owner and
fresh read guard failure. Existing native account, captured title, Fetch-owner
and name-response recovery checks passed. Main Debug built with zero warnings
and errors after verifying the app was absent. No live actor switching, emulator
restart, production-data changes, publication or package. Live recovery remains
unverified. Updated executable: `855Media/bin/Debug/net10.0/855Media.exe`.

Credit this related improvement once: 29/10 → 30/10 toward 3.2.5.
