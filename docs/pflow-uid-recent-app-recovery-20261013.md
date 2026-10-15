# UID recovery through recent-app clearing

When native personal-account observation remains unavailable after profile navigation recovery, the shared navigator clears Android recent apps, launches Facebook, waits for its foreground activity, and repeats the live UID read once. The retry does not reuse prior personal-identity observations.

The navigator is shared by mapped account fetching, account rotation, posting, Screentime and Add Account verification. These app entry points check existing workflow review/upload conditions before recovery. Unmapped account discovery also retries once through the shared recent-app restart helper when its personal UID is missing.

This uses the existing verified Clear all recent-app workflow. It does not clear Facebook storage or submit credentials. Login, checkpoint and active composer/input screens retain their existing handling. An unsuccessful clear, reopen or second UID read stops recovery. A different live UID never verifies the requested account.

Regression coverage includes successful UID recovery, wrong UID after reopening, bounded retry on persistent failure, failed recent-app verification, failed relaunch, blocked workflow review and login-screen preservation.
