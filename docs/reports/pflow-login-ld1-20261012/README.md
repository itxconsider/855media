# LD-1 logout/login validation — October 12, 2026

The requested live logout/login check passed across interrupted runs used to adapt
the observed Facebook and Android layouts. Final state: authenticated personal
account **Carey Northcutt**, numeric live UID **61586742627455**, on LD-1 / FB-01
(`com.facebook.katana`). The UID was read from the current account's own Profile
Settings → Your profile link and confirmed after returning to its own profile.

The test used the production protected credential store, native navigator,
automatic session service, device binding/lease, and idle checks. It enabled
automatic login only in memory for the test. Saved options/selections were not
changed. No publishing or ScreenTime actions ran.

Observed sequence:

1. Initial personal UID inspection passed after navigating from the current Page.
2. The sidebar's Log out footer was tapped from the current Facebook context.
3. **NOT NOW** dismissed Facebook's save-login prompt; **LOG OUT** confirmed logout.
4. **Use another profile** opened the password form. Android's saved-password
   chooser was closed; the different suggested account was not selected.
5. Protected 855Media credentials were submitted. The first login required a TOTP
   code, which Facebook accepted; a subsequent live UID inspection passed.
6. Further test runs exposed post-submit loading and activity-transition races.
   The handlers now observe those states without repeating the password submission.
   Android's **Save password to sign in to Facebook?** prompt is also closed.
7. The final recovery run submitted the password once, completed without another
   OTP challenge, and passed both the automatic session service's UID check and a
   separate fresh post-login UID inspection. The account was left logged in.

Evidence:

- `before-result.json`: successful initial numeric UID proof.
- `after-totp-result.json`: successful numeric UID proof after the TOTP login.
- `interrupted-logout-result.json`: a later logout run stopped during an activity
  transition; Facebook subsequently reached its logged-out password form.
- `recovery-result.json`: final successful automatic recovery, one password
  submission, matching live UID, zero publishing actions. `StartedLoggedOut=true`
  records that it continued the prior logout rather than issuing a new logout.

The focused suite passed **712 tests**, zero failures/skips. The solution build
passed with zero warnings/errors using a temporary artifacts directory because
the normal Debug executable remains open. The running desktop app needs a normal
rebuild/restart to load the latest handler. Only one credential-backed account was
mapped on LD-1, so changing between two different personal accounts was not tested
live. Credential values, OTP values, and authentication screenshots are not stored
in this evidence directory.
