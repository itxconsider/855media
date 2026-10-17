# Posting target session verification

UID verification runs once for the active posting target session. Consecutive confirmed Reel, Video, Picture, and Story jobs on the same device, account binding, batch, and target reuse that session when the current capture contains safe Facebook UI. They skip both another UID lookup and reopening the profile.

The first job uses Graph UID verification with UI lookup fallback and confirms own-profile controls. Session state exists only in memory. Target changes, account preparation, persona selection, ScreenTime, failures, missing confirmation evidence, unsafe UI, and new batches require fresh verification. A missing or changed Facebook process ID also invalidates the session before posting, covering app restarts.

Each job receives a single-use continuation proof valid for 90 seconds. Media transfer consumes it and checks current UI again. Destination selection, Just once, composer identity, the final Share gate, and public confirmation remain required per job. A mismatched composer blocks Share even when the session was reused.

Validation includes ten consecutive posts with one UID verification, target and failure invalidation, proof expiry and persistence checks, and a synthetic two-post publisher test that rejects an incorrect composer. No live public posts were submitted.

Results: all 1,571 LDPlayer tests passed. The desktop app built with zero warnings and zero errors.
