# New account Fetch defaults

After successful account Fetch and Page persistence, register an absent exact
account binding with Fetch, ScreenTime and Posting enabled. Keep automatic login
and rotation disabled. Existing records retain all saved options; repeated Fetch
does not turn options back on. Restored removed accounts retain their restoration
defaults. Registration is atomic, cancellation-aware and fenced against later
account removals/mapping revisions. Mapping refresh then populates Page access
and displays the new options.

43 focused Core tests passed, including four new persistence/preference/removal/
cancellation cases. All 17 isolated desktop scenarios passed, including new
VMDK/VDI account creation, persisted options and actual table bindings. Final main
Debug 3.2.4 built with zero warnings/errors after explicit user closure and a fresh
app-absence check. Credit once, counter 6/10 toward 3.2.5. No production account
options were edited, emulator actions performed or package produced.

Latest real posting log reviewed: LD45 at 20:04 on October 29. Before running its
new queued job, the worker tried cleanup of older manually confirmed job
5f196e0b5c094034964f7db226291a2a. Its account binding failed the current-instance
check. Cleanup remains Pending, and new job 04dcf4831a0341eb97c99dae10154edd was
cancelled before submission when the batch ended. The worker's cleanup scan uses
DeviceIndex only; this can surface old-installation cleanup while working on
another installation with the same index. No cleanup guard was bypassed, history
edited, or job retried here. Installation-scoped cleanup remains separate work.
