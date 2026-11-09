# Owner switching and final publication confirmation retry

2026-11-02, two improvements toward 3.2.5.

## Personal owner recovery

The LD61 failure at 22:36 expected Sue Li while the saved, owned Page Tail Talks+
remained open. The LD9 database contains both the personal owner and this Page.
The profile dropdown did not expose usable owner proof. Its top-left Menu button
was also excluded by the existing feed/Reels-only navigation predicate.

Name-mode recovery now permits a separate Menu route only from an owned profile
whose complete active name uniquely matches a non-pending saved Page with the
current exact account binding and owner UID. A fresh safe frame must contain one
usable, enabled top-left Menu button. The reopened Menu must expose the exact
owner shortcut in its active header row before one shortcut tap. Up to 30 seconds
of guarded observations then require the expected personal header; the existing
native name/type observer independently verifies it afterward. Saved membership
authorizes navigation only. Unknown/foreign/duplicate Page mappings, disabled
ancestors, missing/wrong shortcuts, cancellation and unchanged/wrong final actors
cannot authenticate the account. No Graph, login, guessed coordinates or restart.

## Final timeout recovery

After the regular publication wait expires, Reel/video/photo and Story routes
attempt one final recovery before their timeout becomes Unknown. Under the
existing device lease, verify the job's installation binding and safe Facebook
foreground, clear recent apps with the existing verified helper, relaunch
Facebook, open `fb://feed`, and require a safe recognized feed. Recovery is bounded
to 90 seconds. The existing submission then receives up to 60 seconds of further
confirmation observations. No second Share, media transfer or new job is made.

Retain the original pre-submit notification/UI/Story baselines and submission
identity. Reset transient evidence and settling observations after reopening.
An old work cycle followed by cleared UI is explicitly rejected as confirmation
after recovery: closing Facebook could have removed the pending indicator.
Fresh publication evidence or the existing target-profile/own-Story verifier is
still required. A failed/unsupported recovery leaves the normal Unknown outcome
and review/cleanup holds. Visible failures, cancelled operations and foreign
foreground cannot authorize recovery. No app-data deletion is used.

## Validation

- 221 focused Core checks passed, including eleven new owner recovery cases and
  five new confirmation recovery cases; existing native account, captured title,
  owner recovery, confirmation, publication cycle/settling/feed/failure checks.
- After the final visible-failure guard, all five recovery cases passed again.
  Three actual runner publication cases passed: pending copyright scan waits,
  failure takes precedence over success, and upload notification confirmation.
- Isolated desktop Debug build: zero warnings/errors, output under
  `artifacts/switch-match-desktop`. Main app PID27384 was running, so its output
  was left unchanged. No live actor switching, restart, publication, production
  data edit or package. A live retry remains unvalidated; use the updated build
  after the current run finishes.

Credit the two related changes once each: 27/10 → 29/10. Packaging adds no credit.
