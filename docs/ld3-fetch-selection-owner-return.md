# Fetch selected-row matching and personal-profile return

Completed 2026-11-03. Compared Fetch with posting's exact-name/type/selected-row matching. Notification suffixes were already stripped from row names; notification counts were not an identity requirement. Fetch nevertheless rescanned the entire list between successive selected-row observations, moving the selected actor offscreen and sometimes dismissing the sheet during subsequent top-seeking gestures. It could also try the profile-settings dropdown before leaving that surface.

Fetch now carries invocation-local verified membership into selection, rebinds two consecutive fresh selected-row frames without scrolling between them, and retains the final complete-list/name/type check in the adapter. Fresh rows must belong to the known membership; duplicate/foreign/disabled/ambiguous selections and positive Page-badge contradictions against the personal profile are rejected. Notification suffix changes are ignored. A complete initial scan that leaves the target offscreen reopens the sheet once. Cached selection searches down from a visible selected row instead of dragging the sheet down. Profile settings is left before opening the discovery switcher.

A second captured defect affected Page-to-personal restoration: SAM Outdoor's notification row was clipped to99pixels at the screen bottom, exceeding the old96pixel visibility threshold while its Page badge was hidden. Its later revealed badge contradicted the incomplete observation. Rows touching the RecyclerView's screen-bottom clipping edge now require another revealed observation rather than supplying type evidence.

Validation:

- All160 focused type-inference, badge-free selection, native Fetch/device and discovery-recovery checks passed. Seven new cases cover the captured Page-selected scan, matching parity with posting despite notification changes, and duplicate/disabled/foreign/two-selected/unknown-member rejection. The selected-Page scan regression failed before the clipping fix and passed afterward.
- Live production Fetch adapter began on selected YeeYee's Life, scanned six actors, returned to personal Harch Jing with SelectAsync=true, and verified Harch Jing again with SelectAsync=true. The Menu header independently read `Harch Jing, see your profile`. Graph0; keyboard restoration=True; saved stores read-only; no publication. Left the device on its personal profile. Evidence: `artifacts/ld3-return-owner-fixed/native-fetch.log`, step captures and final capture.
- Main Debug and diagnostic builds completed with zero warnings/errors after main app-absence verification. Executable: `855Media/bin/Debug/net10.0/855Media.exe`. No release package/version change.

This live test certifies the previously failing Page-to-personal return and already-active personal verification. Full UID/details Fetch, persistence and switching through every Page were not rerun. LD9/LD14 recent-app cleanup behavior is unchanged.
