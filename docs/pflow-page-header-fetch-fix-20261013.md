# LD-31 Page fetch validation — October 13, 2026

The Page switch completed on Luke Kaz, but the actor check compared raw labels.
Facebook exposed the header name with trailing newlines and nonbreaking spaces,
so the app rejected the correct Page and skipped all subsequent detail lookup.
Actor matching now normalizes whitespace and still requires an exact name or
an exact Posting as / Sharing as label. Other names and verification badges
do not satisfy that match.

A second issue prevented the header-to-transparency route: an Edit profile
toolbar pencil above the cover photo was treated as the bottom of the header.
For Page layouts, the reader now uses the Professional dashboard / Manage Page
boundary below the name. It still rejects same-name post authors below that
boundary.

The final live fetch on LD-31 ran through the actual 855Media app at approximately
01:58–01:59 Bangkok time. Both Page rows were saved with verified access:

| Page | Creation date from transparency | Followers | Views |
| --- | --- | ---: | ---: |
| Luke Kaz | August 25, 2026 | 3 | 4.8K |
| Z Shortdrama+ | September 12, 2026 | 0 | 166 |

The app returned to Friedrich Ludwig's personal profile. LD-31 remained running;
the temporarily disabled CloseLDAfterAction preference was restored to true.
No publishing or ScreenTime session ran.

Validation: 809 focused PFlow/ScreenTime tests passed after the actor fix.
After the header correction, 75 relevant actor/switch/header/date tests passed.
The final solution build passed with zero warnings and errors.

An initial retry from the Feed stopped during personal UID verification before
Page lookup. Opening the current personal profile allowed the subsequent fetch
to complete. This separate Feed recovery limitation is not resolved by these
Page fixes. Live verification was limited to LD-31; other devices were not rerun.

Evidence remains in %AppData%/855Media/logs/facebook-fetch.log and p-flow.log.
The saved rows were checked in %AppData%/855Media/facebook_pages.json.
