# Story workflow screen variants

Reviewed the four screenshot collages and the October 14 batch `54a41c2998b24a949d6dfaae6fcc4e86` in `%APPDATA%/855Media/logs/p-flow.log`, plus its saved XML diagnostics.

| LD | Logged failure | Correction |
| --- | --- | --- |
| 1 | Own-profile controls not confirmed | Accept the existing verified personal-profile recognizer's cover/profile photo edit controls on professional profiles with Dashboard |
| 13, 14, 15, 17 | Feed Create control missing | Use the observed enabled Create story card when header Create is unavailable |
| 11, 12, 19, 20 | Submitted, then publication confirmation timed out | Recognize enabled Your story cards inside the observed passive disabled Story-tray wrapper, restoring baseline/viewer access for future runs |

Saved XML playback with the corrected recognizers found a usable Create story card and an empty loaded-tray baseline on all four navigation failures. It found a usable Your story entry in all four unconfirmed-job captures. LD-1's saved profile is recognized as its own personal profile. This does **not** establish that any of the four submitted jobs published successfully, and no existing job was reconciled or retried.

The disabled-wrapper exception is restricted to a non-clickable ViewGroup directly containing a RecyclerView inside a labeled Story tray. Disabled controls, hidden ancestors, unsupported wrappers and clipped controls remain unavailable. The existing picker GridView wrapper handling is retained.

The editor accepts either Audio or Music, retaining Text, Stickers, discard controls, and a unique usable Share target. Public/Friends audiences are left as selected. The empty picker variant accepts Create story + No photos on this device + an observed album spinner without Select multiple. The workflow still requires the exact staged job album, MediaStore path/MIME/size/checksum and one matching item. It waits up to 12 seconds for two stable observations and takes a fresh picker observation immediately before the media tap; it never selects an arbitrary Gallery thumbnail.

Logging now records the chosen entry route, baseline entry/empty-tray evidence and the last registry diagnostic on layout timeouts. Publication confirmation still requires attributable fresh success evidence or a verified one-story count increase from a known baseline. Unknown baselines and old submitted jobs remain unconfirmed.

Validation covers captured XML replay, simulated dry runs through scrolled feeds, delayed empty galleries, Audio editors and professional personal profiles, plus disabled/hidden controls and ambiguous Share controls. A read-only current-device capture sent no input; it observed launcher surfaces rather than fresh Facebook editors. No live Story was published for this repair.

Updated desktop output: `artifacts/story-variants-fix/855Media.exe`.
