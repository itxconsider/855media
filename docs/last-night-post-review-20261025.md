# Last three overnight posting batches

Reviewed 2026-10-25, Bangkok time (UTC+7). Sources: `%AppData%/855Media/logs/p-flow.log`, `facebook-fetch.log`, saved target names and the final failure's captured native XML. Outcomes are deduplicated by job ID; repeated cleanup/status lines do not count as extra posts. This is a log review, not a live posting test.

| Batch | Last outcome time | Result |
| --- | --- | --- |
| b78c5b7bd81e4c628cd8621cb6bfcc76 | 02:34:31 | Six cancelled before submission; no job reached posting. |
| d321ac09040d42d8965035d0c864300f | 02:35:50 | Six cancelled before submission; no job reached posting. |
| a0f89ce09256431bb142985faf0633e0 | 02:56:37 | Three confirmed, one failed before submission, two cancelled before submission; LD-56. |

The first two batches show dropdown navigation and one account recovery restart, then batch cancellation. Their job records only say 'Batch ended before this job ran'; the logs do not establish whether this was a user Stop or a startup failure. Neither batch should be called six failed uploads.

## Final LD-56 batch

| Target | Reserved | Share acknowledged | Confirmed | Share-to-confirm |
| --- | --- | --- | --- | --- |
| KCWaterheaters | 02:36:58 | 02:37:30 | 02:38:08 | 38 seconds |
| Bros Cooking | 02:38:09 | 02:38:39 | 02:40:51 | 2m12s |
| Survival Outdoors | 02:40:53 | 02:41:52 | 02:56:27 | 14m35s |

Survival Outdoors remained in the publication wait loop with repeated Android Facebook upload-ongoing notifications, intermittent pending-label-clear observations and Home refreshes about every 30 seconds. The final pending-clear refresh was around 02:55:54; the app accepted its observed publication-cycle evidence at 02:56:27. The delay was after Share, rather than a Graph identity lookup. These logs do not distinguish actual slow upload from stale Facebook notification/status evidence.

The next Survival Outdoors Reel started immediately after cleanup (reserved about 02:56:30) and failed around 02:56:36: 'Posting target name/type could not be selected in Facebook.' Native navigation tapped Menu at 02:56:30 and its dropdown at 02:56:33. The failure XML still shows the Menu with 'Survival Outdoors, see your profile', 'Marc Aurelius, switch into your profile' and 'Open profile switcher, you have notifications'; it does not show an opened actor sheet. Thus the captured failure is dropdown/switcher navigation, not evidence of a wrong name or an extra successful Page switch. The two Z Outdoors+ jobs were subsequently cancelled before submission.

## Graph calls

Final-batch account and three target identity traces explicitly state Graph calls=0/live UID lookup=0. No Graph request activity appears in either log for LD-56 between 02:33 and 02:57. This supports the zero-Graph posting code path; these logs are not an independent HTTP/network audit and do not count Facebook's own internal traffic. The legacy `account.uid.recovery.restart` label in the first two attempts describes a restart helper, not proof of a Graph call.

The selected-name identity change documented in `posting-selected-name-policy.md` removes the old button gate. It has not yet been live-tested against the final dropdown-opening failure, so this review does not claim that failure is resolved.
