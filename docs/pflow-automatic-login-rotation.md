# Automatic login recovery and account rotation

Date: October 12, 2026

Implemented in `D:/repos/855Media`. The solution builds and the focused regression
suite passes. Live LD-1 logout/password-login recovery with protected credentials
and numeric personal UID verification passed on October 12. One account was
available for this check; live rotation between two different accounts remains
unvalidated. No public posting or ScreenTime actions were run in the login test.

## Configuration

Open **Targets & media → Personal accounts**. For each mapped account:

1. Store its credentials with its supported login identifier (UID, email or phone
   as accepted by the installed password form). Credentials remain protected by
   Windows DPAPI and the exact LD/app/owner binding.
2. Enable **Automatic login** to recover an expired login.
3. Enable **Account rotation** to permit logging out the current personal account
   and logging in as this incoming account.
4. Set **Run order** and the separate Fetch, ScreenTime and Posting selections.
5. Save account selections. Lower order runs first; tied orders use owner UID.

These options default off for existing configurations. Storing credentials alone
does not enable login or any public action. New fields are backward compatible
with version 1 of `pflow_accounts.json`.

After clearing/replacing an LD, reviewing the same account's new binding retains
its protected credentials and login options. The saved credentials are read using
the old binding, protected again for the reviewed replacement, and committed with
the new mapping. Task selections and identity/access proofs are reset. A failed
transfer leaves the original mapping and credential reference unchanged. Already
authenticated Facebook sessions can run after live UID verification without stored
credentials.

The credential dialog loads the stored login identifier, full UID/password/2FA
line and factor type automatically, retains them after Save, and clears the visible
values when Close is clicked. Records remain protected by Windows DPAPI; values are
not added to account configuration or diagnostics. Stored credentials can also be
viewed when an instance needs review, while saving requires a valid mapping.

Fetch and Start now persist the visible personal-account selections for their
selected devices before planning work. Save account selections persists choices
without starting work. Login/rotation controls are unavailable until protected
credentials exist, and a ScreenTime-only run with no selected mapped account
reports a preparation error instead of silently skipping.

## Execution

One device lease covers the entire batch on an LD. Different LDs may run concurrently.

```text
Verify Profile A (reuse its existing session when possible)
  → A's selected ScreenTime
  → A's queued profile/Page posts
  → resolve publication outcomes and return to A's personal profile
  → load B's protected credentials before logging out A
  → log out A and open the recognized password form
  → submit B's login once; handle configured authenticator/TOTP
  → verify B's live personal UID
  → B's selected ScreenTime and profile/Page posts
  → final cleanup according to run settings
```

The final return-to-profile preference is preserved. Intermediate accounts must
return to personal context before rotation. Mapped Fetch uses the same login
service sequentially and scopes root candidates and Page enrichment to the exact
verified owner. Incomplete Page enumeration never establishes that access was removed.

Queued tasks now contain owner UID, canonical target ID, target type and account
binding. Legacy rows with ambiguous names or shared-Page ownership are excluded.
Publication duplicate protection remains target/content/type based across owners.

## Recovery and stopping conditions

- Reuse an authenticated session without loading credentials or logging out.
- Recover a recognized expired password form or supported saved-login landing.
- Allow at most one login attempt for an account binding per batch, including
  unsuccessful or uncertain attempts. Credentials are disposed after the attempt.
- If posting fails before submission at an explicit login gate, recover the
  session and retry that job once. A possible submission is never automatically retried.
- Unknown/Submitted/SubmitRequested posting history, unresolved ScreenTime actions
  and recognized ongoing Facebook uploads prevent further tasks and account rotation.
- Wrong UID, changed binding/network, rejected credentials, CAPTCHA/human checkpoint,
  approval without a supported authenticator alternative, and unsupported screens
  stop the affected device's work. Authentication failures leave Facebook open.
- ScreenTime interrupted after it starts is not automatically replayed; public
  interactions and their quotas must not be repeated by restarting a whole session.

## Identity adapter and current limits

The native adapter opens the current actor's own profile, then Profile settings,
and reads the numeric **Your profile link** before restoring the own profile.
Supported links are `facebook.com/profile.php?id=<UID>` and
`facebook.com/people/<name>/<UID>/`, including the www/m host variants.
Public/feed links, Page links, mixed links, editable overlays and cached root UIDs
cannot establish this proof. Root metadata may suggest a personal-navigation route
when a Page is active, but a fresh own-profile link must still establish the UID.

Vanity-only links and other installed layouts stop for user action. LD-1 confirmed
the professional personal-profile layout, **More profile settings**, numeric link
below the fold, the selected Menu tab after scrolling, and the logout footer above
fixed bottom navigation. The logout route handles **NOT NOW** in the save-login
dialog, **LOG OUT** in the confirmation, and **Use another profile** on the saved
account landing. All controls use fresh UI bounds; displayed account names do not
replace UID verification. Tests use constructed fixtures based on observed labels;
they do not certify every installed Facebook version.
FB-01 (`com.facebook.katana`) is the supported package. Password and configured
TOTP recovery are automated; unsupported SMS/email/security challenges remain manual.

Android's observed Google credential chooser is dismissed with its explicit Close
control for both **Use your saved password for Facebook** and **Save password to
sign in to Facebook?**. Login uses only the account's protected 855Media credential
record. Android credential-manager labels/values are redacted in diagnostics.
Known activity transitions and the still-visible submitted login form receive
bounded observation waits, without submitting the password again.

Fresh LDs automatically install the configured/bundled ADBKeyboard when it is
missing, verify the package, enable it, and verify the active IME before entering
any login identifier/password. An installation or activation failure stops login
without entering credentials.

The user's location-onboarding preference is to choose **Continue**. The handler
recognizes Facebook's exact Location Services introduction and, when Android asks,
accepts only Facebook's location permission through the scoped package-installer
dialog. It rejects unrelated permissions/apps and still requires exact live UID
proof before running tasks. Facebook **Save your login info?** prompts use **Not
now**; Google saved-password/save-password prompts use **Close**.

**Preview without publishing** prevents Share/Post. It can still change the active
login, and existing ScreenTime public-action opt-ins remain effective.

## Verification

### Automatic personal-profile recognition and recovery (October 12)

When a live identity check is unavailable on a recognized own-profile, Page,
settings or Menu surface, the native navigator makes one additional recovery
attempt to the mapped personal profile. Page and personal profile screens can
share Edit profile controls, so those controls alone never prove the account.
After recovery, the navigator reads the numeric UID from fresh personal settings
and restores the own-profile surface. A different UID is reported rather than
replaced by the requested UID. Login forms, checkpoint screens, editable inputs,
composers and unrecognized screens are not used for this recovery.

Recovery does not log out or submit credentials. Login/rotation still use their
existing explicit account settings and exact identity checks. The fetch status
now distinguishes Facebook verification challenges from an inability to confirm
the personal UID. Added fixtures cover successful, failed and wrong-UID recovery;
a live LD-1 inspection verified Carey Northcutt, UID `61586742627455`.

The combined PFlow/ScreenTime/AtxAgentClient suite passes **712 tests**, with zero
failures or skips. The solution build using `dotnet build 855Media.slnx
--artifacts-path <temporary build directory> --verbosity quiet` passes with zero
warnings/errors. The temporary output avoids replacing assemblies held by the
currently running 855Media process. Close the application, rebuild its normal
Debug output, and restart it to use these latest live-layout fixes.

The LD-1 check completed the requested logout/login sequence across interrupted
runs while adapting the observed layouts. The final recovery began at the
password form, submitted the password once, reused Facebook's established device
trust without another OTP prompt, and verified UID `61586742627455`. An earlier
attempt also submitted one TOTP code successfully and its subsequent personal UID
inspection passed. The account remains logged in. See
[live evidence](reports/pflow-login-ld1-20261012/README.md).

Remaining live acceptance: two mapped profiles on one LD and a preview run through
each profile's Page composer. No controlled publication was performed for this change.
