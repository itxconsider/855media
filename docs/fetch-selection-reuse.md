# Fetch selection reuse and switcher handoff

2026-11-05, Asia/Bangkok.

The latest LD14 traces showed several layers verifying the same actor independently. Fetch selected the owner, read its link, selected the owner again for personal details, then selected a Page and invoked a legacy Page-details reader which selected it again. Row confirmation also closed its verified switcher sheet immediately before the adapter reopened that sheet for the final complete-list check.

## Changes

- Keep invocation-local selected-name/type evidence only after the existing completed-list verification succeeds. A repeated request for that actor can skip switcher navigation after two fresh own-profile observations with unchanged validated Facebook process set, foreground and safe UI. Different actors still take normal selection; Configure, failed context and changed actor/process invalidate reuse. Nothing is persisted or reused for posting.
- Hand the still-open verified selection sheet to the adapter's completed-list scan. The complete scan, type/membership/unique-selection checks and final close remain; one close/reopen cycle is removed. Other SelectFetchRow callers retain their old close behavior.
- Carry a one-use service/index/installation/owner/Page-ID/name-bound handoff into the Page-details reader. It must freshly open/recognize the already-selected own profile and check it twice before confirming selection in the existing Page-fetch state machine. Failed proof stops the reader; ordinary readers outside this scope retain normal switching. Own-profile, date and dashboard guards remain.
- A successful personal-details return to recognized Feed clears the pending native-link navigation obligation. Page-details handoff satisfies any remaining native-link Feed obligation before opening the profile, preventing settings controls from being treated as navigation tabs.
- Recognize the native Page/Profile settings toolbar even when scrolling hides its link/status rows; close that owned settings stack on unavailable-link outcomes. The captured LD14 settings regression exercises the actual reader and confirms one Back and one required Feed return.
- Normalize process identity as the existing validated unordered numeric PID set.

## Validation

All157 focused Core checks passed, including21 new cases across actual adapter/legacy-consumer handoff, routing/scope mismatch, one-use behavior, disabled/unsafe context, process changes and PID reorder, cancellation, already-open-sheet scan, Feed-obligation deduplication and captured scrolled settings cleanup. Evidence: artifacts/fetch-selection-reuse/tests/completed.trx.

The initial full API-off LD20 probe confirmed owner and Page selection and exercised owner-profile reuse. Native clipboard preparation failed and Page-link lookup returned unavailable. The initial handoff then exposed a missing settings-to-Feed reset: profile navigation reached Search this Page and stopped at its input guard. That new handoff boundary is now guarded and the captured cleanup regression passes. That initial probe did not complete Fetch or owner restoration; explicit read-only recovery subsequently restored Anna through the production owner-selection diagnostic. This full run is not a successful end-to-end speed benchmark.

The focused API-off LD27 production-adapter probe used its complete native list and saved routing IDs to test Page selection → details handoff. It verified Plumb N Plumber, continued without a second Page selection, read November1,2026 and zero followers, then restored Bryn Wright. The separate dashboard tap still timed out and views remained unavailable. Graph0, no publication or production table persistence. The positive live handoff predates only the final settings/Feed hardening; that boundary is covered by the final captured regression.

Measured LD27 phases (one already-active Page; not a representative new-switch average): initial complete scan30.38s; selected-Page verification39.57s; Page details105.99s including the dashboard timeout. Selection start through details completion148.60s (2m29s). The diagnostic adds captures between stages and omits UID lookup in this focused mode, so this must not be presented as a general replacement for the earlier3m18s average. The structural reduction in switcher reopen actions is directly verified by the adapter test.

Main Debug3.2.4 built with zero warnings/errors after a fresh app-absence check. Tested Core matches main SHA256 ED1003E6B25555CB40E175D2C35FDB37B18515FFB7A5317D6F6BB27EAA9146F1. No production account/Page/history edit, publication, Graph request, LD9/LD14 Recents change or release package. Native clipboard/UID lookup, dashboard navigation, other LD14 instances and a representative multi-Page speed benchmark remain outside this validation.
