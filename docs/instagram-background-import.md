# Background Instagram import

Switching tabs now hides the main Instagram browser and stops profile auto-scrolling without cancelling an in-progress Import. The independent post-detail renderer retains its existing session and resolves the snapshotted post list sequentially. Returning to Instagram shows ongoing progress and the existing Stop button.

Explicit Stop/Clear, navigation/refresh, subscription-expiry stop notifications, view detachment, changed DataContext and VM disposal still cancel import. Completion still opens the existing media/destination selection dialog, including when another tab is active; no automatic destination is chosen.

Validation (2026-10-30): actual view headless lifecycle checks verify tab deactivate/reactivate preserves the import token and IsImporting, explicit VM Stop cancels, and removing the view cancels. Existing responsive diagnostic passed access/Stop/Clear, native-browser exclusivity, modal suppression/restoration, layouts and grading checks. Isolated desktop/diagnostic build passed with zero warnings/errors. All 55 Instagram Core tests passed. No live signed-in background import certification; main running app and package unchanged. Build: artifacts/instagram-background-build.
