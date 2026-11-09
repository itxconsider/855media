# LD55 switcher investigation

2026-11-05, C:\LDPlayer\LDPlayer9, index55.

The 00:40:38.635 dropdown tap was at539,148, inside the captured enabled/clickable Open profile switcher control. The job failed at00:40:41.227 without logging layout.switcher, native.switcher.not-found or native.identity.row. The post-failure capture was a recognized Menu with no input/authentication/foreign-app blocker. It cannot reconstruct the intermediate frame or tap response.

The production opening loop exits immediately on an unsuccessful TapAsync acknowledgment or a blocked intermediate frame. A normal failure to open would consume its opening budget and log native.switcher.not-found. The available historical log does not distinguish those early exits. The previous suggestion of failed acknowledgment remains a hypothesis.

Live diagnostic, under installation/index device lease and existing pending-history/upload-notification checks: freshly recognize Menu, bind the exact production switcher button, tap once, capture subsequent frames without selecting any actor, then dismiss the recognized sheet and verify Menu restoration. Tap acknowledged=true; the first observation after500ms matched switcher.typed.v1. Actors were HDPlumbers+ (selected Page), Scarlet Marcello (Profile), Bushcraft improvisation (Page). The requested target was present and enabled. Menu restoration passed; Graph0; no submissions or actor selection.

An initial stricter diagnostic precheck refused the Menu's disabled non-clickable ancestor despite the enabled button. Production's semantic button finder does not apply that ancestor check, so this is not established as the historical failure cause. The explicit single-button diagnostic subsequently succeeded. No production guard was changed.

Evidence: artifacts/ld55-switcher-investigation/current.xml and probe/switcher-probe.json plus probe/frame-00.xml. Diagnostic build zero warnings/errors. No production runtime change, main desktop rebuild, release credit, package, mapping/history edit or publication. New --switcher-open-probe diagnostic is limited to a recognized Menu and existing device/history/upload guards. Original failure remains unreproduced; future diagnosis needs tap-result and exact early-exit diagnostics at the production boundary.
