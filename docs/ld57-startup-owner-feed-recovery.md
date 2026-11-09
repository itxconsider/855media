# Saved-owner startup from a hidden Feed

2026-11-05, Asia/Bangkok; C:\LDPlayer\LDPlayer9, LD57.

The 01:07 failure occurred before posting, while startup expected Tani Charada (Profile). Its captured screen was a recognized stacked-create Feed with no usable navigation controls. The previous Page-selection fix did not cover this startup discovery path. Discovery exhausted three header-reveal gestures but kept choosing Reveal without another gesture or Feed reopen. The final legacy personal-target fallback also did not provide the complete-list, binding-aware recovery available for a blocked owner Menu.

## Change

Name-mode startup with saved targets now routes a freshly recognized, non-pending Feed through the same shared saved-owner recovery used by the blocked Menu case. It revalidates installation/worker binding, foreground, process, input/authentication, publication idle state and cancellation. It reads the complete actor list, requires a unique exact personal owner and consistent membership/types, selects only that owner, reopens and rechecks the complete list, then retains the existing final name/type observation. Feed recognition alone grants no identity proof.

Discovery can reopen Feed once after three ineffective reveals, only with a recognized Feed and Facebook foreground. It uses fresh controls afterwards and retains the reveal budget. The guarded opening method calls the supplied freshness check during navigation and switcher settling. The original three-argument method remains as a wrapper for existing callers and diagnostic reflection.

The guarded opening budget is60seconds because freshness checks include real device reads; normal opening remains30seconds. Startup from Feed uses a linked three-minute outer recovery budget, while existing blocked-Menu recovery retains two minutes. Cancellation remains immediate through the linked token. A Feed recovery timeout reports that final identity confirmation timed out, rather than implying the expected name was different from an identical observed name. The automatic account flow still has its existing five-minute outer deadline.

## Verification

- Captured fixture: docs/fixtures/pflow-layouts/ld57-startup-owner-hidden-feed.xml.
- 141 focused Core checks passed, including twelve new cases covering successful actual startup, one failed recovery, process/worker/foreground changes, wrong owner, conflicting owner mapping, disabled target, type change after selection, new pending publication/PID change after reveals, and seven-second freshness-read delays.
- Reports: artifacts/ld57-owner-startup/tests/verified-timing.trx. The earlier focused runs passed138 and140 checks before the last two guard cases and slow-read case were added.
- A temporary pre-recovery comparison reproduced the slow-read regression. Its broader owner matrix was stopped because the pre-fix startup path uses real waits in its legacy observation service; the partial aborted report is retained in before-recovery.trx. Fixed source was restored in finally. No claim is made that this partial baseline matrix completed.
- First live attempt from the original hidden Feed reopened Feed after three ineffective gestures and selected Tani, but hit the original two-minute deadline before final proof. It failed with expected/observed both Tani Charada (Profile), Graph0; keyboard restored. This exposed the real-read timing issue addressed above. Evidence: artifacts/ld57-owner-startup/live.
- Final live test staged the previously active saved Page, Botanik Bushcraft, then its scrolled Feed. Production automatic startup completed and freshly verified Tani Charada (Profile) at01:24:05; saved-account startup passed at01:24:06. A subsequent owner-preparation check also passed at01:24:11. Graph0, no submission, keyboard restored. Saved stores were read only; the diagnostic's account-state changes were in memory. Evidence: artifacts/ld57-owner-startup/live-final/native-fetch.log and saved-startup captures.
- Desktop Debug3.2.4 and final live diagnostic built with zero warnings/errors. Their Core SHA256 matches: E45AD38CC93D03676D711DC3FF35BF2AAC124C86ED5B926AF32762A4E8084C7A.

The existing desktop was running; its main output was not overwritten. Updated executable: D:/repos/855Media/artifacts/ld57-owner-startup-build/855Media.exe. No publication, token read, production mapping/history edit, Recents implementation change, release package or shutdown. LD57 was left on the verified personal owner after the user-authorized navigation test.
