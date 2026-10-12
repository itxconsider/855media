# ScreenTime after an LD reset — October 12, 2026

The reported LD-1 row had visible Fetch/ScreenTime selections while its stored
selections were all false. Its reset binding had no credential reference, but the
previous Automatic login and Account rotation flags remained true. Saving account
selections rejected that combination, and Fetch/Start read the old unchecked
selections. A live inspection also confirmed Facebook was at its password login
form, with no authenticated numeric UID available.

Changes:

- Remapping an LD clears both login/rotation permissions along with old credentials
  and task selections. The old device's credential binding is not reused.
- Account cards show login/rotation options disabled until their prerequisites
  exist and normalize stale options when displaying a credential-free account.
- Fetch and Start persist the visible selections for the devices they will use
  before loading and freezing the account plan. Other devices' drafts are untouched.
- Selection updates validate every draft before changing any account, reject stale
  bindings, and report which account needs attention. Manual save errors are also
  shown in a notification.
- A ScreenTime-only run with no selected mapped account reports a visible error.
  Preparation errors update ScreenTime and selected-device statuses.
- The live inspection tool can verify an existing session without stored credentials.

Validation: 723 focused PFlow/ScreenTime/AtxAgentClient tests passed, no failures or
skips. The solution built to normal Debug output with zero warnings/errors. The
updated app was launched for the user to store credentials on the reset binding.
No ScreenTime public interactions were run during this verification. Completing
live login and starting the user's session requires the user to finish credential
import in the application.
