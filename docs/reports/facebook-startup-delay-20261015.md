# Facebook startup timing

Investigated LD-1 and LD-3's `Assigned Facebook app did not open` errors around 03:36 (Asia/Bangkok). They stopped in preparation before live UID verification. LD-2 logged `identity.live-uid method=Graph UID` at 03:36:56 and completed ScreenTime at 03:40:23. Read-only captures subsequently showed Facebook Feed on LD-1 and LD-3, so the old launch failure did not establish a missing app. The original run did not log the raw launch failure or foreground state; its precise cause remains unproven.

Saved settings were 30 seconds After LD opens, 10 seconds Before opening Facebook, and 10 seconds Between LD starts. The worker already honored the LD delay, but the active ScreenTime/PFlow preparation route did not consume its snapshotted DelayOpenApp value. The UI explicitly labels that value as **Before opening Facebook**, so it remains a pre-launch delay rather than being silently reinterpreted as a post-launch delay.

The fix captures that setting in the immutable run configuration and applies it to account preparation, ScreenTime preparation and restoring Facebook from Android Home. Startup now checks the assigned package's actual foreground activity after launch, polling through a temporarily blank focus or Android launcher. A false launch acknowledgment can succeed only if the actual Facebook foreground is subsequently observed. A true acknowledgment alone cannot pass. One retry is permitted after rechecking connectivity and the device UI agent; another foreground app stops startup instead of being dismissed. Supported Android credential prompts retain the existing explicit handling.

Logs record `startup.facebook.launch`, `startup.facebook.ready` and `startup.facebook.not-ready` with acknowledgment, delay, attempt and foreground-verification results. No session data is cleared. The fresh Graph/UI UID check still follows preparation and must pass before browsing or publishing.

Regression tests cover configured delay ordering, cancellation before launch, slow foreground appearance, false acknowledgments with a valid foreground, true/false acknowledgments with a persistent launcher, foreign apps and the single-retry limit. No live ScreenTime run, post, app-data clear or emulator restart was performed for this fix.

Desktop output: `artifacts/startup-ready-fix/855Media.exe`.
