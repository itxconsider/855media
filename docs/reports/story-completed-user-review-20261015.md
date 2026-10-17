# Completed Stories: user reconciliation

The user confirmed that all Stories in the displayed batch completed publication. Matched batch `3393914c58824909925e572572add64b` and its exact four remaining Unknown records by LD, target name and source filename.

| LD | Target | Job |
| --- | --- | --- |
| 1 | Carey Northcutt | 71617173ba644cce8eca2b0ffb741188 |
| 2 | Gerardo Lopez | b4d82eda9b374a6ca611883541049a5d |
| 3 | Harch Jing | 7acfe86dd8b648ea9e2f27564b81d55d |
| 20 | William Rhodes | 5abd9ccbfe98435d9682ceaf0c0c375d |

Recorded these four as Confirmed with explicit `Manual review:` evidence attributing verification to the user's October 15 statement and screenshot. Confirmation timestamps represent review time, because the exact publication times were not supplied. Previous SubmittedUtc values were preserved. A consistent SQLite backup was made under `artifacts/story-user-review/` before updates; the normal history SaveAsync path wrote audit events. No media cleanup, deletion or publication was executed. Other batches were unchanged.

LD-1/2 diagnostics show Story ads (Muse) during playback, which caused the cleanup recognizer to reject the new surface. Post-submission playback cleanup now retains the previously captured publication evidence when Back fails or playback changes. It still refuses further input on an unknown screen. Pre-submission baseline navigation remains strict.

LD-20 showed baseline zero followed by three active Stories. This is ambiguous for count-based automatic confirmation; the user's explicit verification resolves this attempt. To reduce transient false zero baselines, an empty tray now requires separated observations before recording zero. Direct recognized own-viewer evidence can also be evaluated if playback remains open.

Updated app: `artifacts/story-confirmation-fix/855Media.exe`. Restart/reopen the app to reload the reconciled database state. This change never retries these completed posts.
