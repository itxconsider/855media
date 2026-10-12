# LD-74 authorized login test — 10 October 2026

## Current execution status — 10 October 2026

The session workflow is partially implemented in both checkouts. The installed authenticator adapter, login screen classification, startup/network checks, immutable session binding, account selection safeguards, Manage accounts navigation and input privacy protections are implemented and tested. Exact live numeric UID verification and saved PERSONAL account switching are still missing, so automatic login and multi-login execution remain guarded.

LD-74 now shows a human-verification checkpoint following the historical successful login. No checkpoint action or new login submission was performed during this execution. The user must complete it manually and provide an app with two personal logins for switching fixtures. See docs/pflow-session-workflow-handoff.md for changed files, final verification and concrete next steps. Earlier sections describe earlier states.

## Latest retry: same reported VPN city — successful

The user reported both devices were connected through Houston and explicitly requested a retry. The live LD-74 form was empty when inspected; the supplied UID and password were entered on that instance only. Android internet was validated. One password submission reached the other-device approval challenge, without the previous Wrong Credentials alert.

Facebook offered Authentication app through Try another way. Its actual six-digit Code field and Continue control were observed. A draft nearing expiry was cleared before any OTP submission; a fresh locally generated TOTP was then submitted exactly once. Facebook advanced to Save your login info?, and Not now was selected rather than enabling cloud credential backup. The personal profile Leon Gallagher opened with own-profile editing controls. LD-74 remains logged in on that profile.

The screenshots show matching VPN city labels; the test does not establish the same exit IP or prove the location caused the earlier rejection. No VPN settings were changed by the agent. No public social actions, account recovery, password guesses, or additional login retries were performed.

Exact live numeric UID was not exposed on the observed profile screen. No PFlow binding or protected credential entry was committed. The default app navigator still requires installed OTP and exact UID adapters; this successful observed ADB login does not validate the app's automatic login flow.

New sanitized fixtures: ld74-facebook-581-approval.xml, ld74-facebook-581-confirmation-methods.xml, ld74-facebook-581-authenticator.xml, ld74-facebook-581-code-accepted.xml, and ld74-facebook-581-own-profile.xml under docs/fixtures/pflow-login. All input values and supplied secrets are removed; no OTP was saved.

Next: implement the observed challenge selectors with foreground/package, account, freshness and expiry guards; obtain supported exact live UID evidence before enabling account/credential commit; run focused adapter tests and both builds. Historical failed attempts below remain valid for their earlier state.


User explicitly authorized testing the account supplied in this chat on LD-74.

## Result
The live test used observed Android controls and ADB text input. It did not bypass or exercise the default app navigator, whose capability guard remains intact.

- LD-74 running; Facebook 581.0.0.45.58; validated Android internet.
- The supplied UID had no existing PFlow account binding. Other LDs were not used.
- Opened I already have a profile and dismissed the saved-account chooser.
- First submission: supplied email and password. Facebook responded: Wrong Credentials / Invalid username or password.
- Second submission: supplied UID with the same password. Facebook returned the same error.
- Identifier text matched the supplied value; password was masked and its length matched. Exact password character round-trip could not be read while masked.
- No authenticator, SMS, email OTP or checkpoint screen was reached. No OTP was generated/submitted to Facebook.
- No password guesses, recovery actions, account changes or social actions were performed.
- Both input fields were cleared and confirmed empty after testing. No account binding or protected credential entry was saved.

## Evidence
- docs/fixtures/pflow-login/ld74-facebook-581-login-filled-redacted.xml
- docs/fixtures/pflow-login/ld74-facebook-581-wrong-credentials.xml

Fixtures redact EditText values and supplied credentials. Raw UI dump files used during inspection were removed immediately after reading. No credential/code screenshot was taken.

## Remaining work
A working Facebook login is required to obtain the actual OTP challenge and exact live personal UID fixtures. The response does not distinguish an incorrect identifier, password, or account state. The automatic-login capability guard remains in place. Verify the supplied login details or complete login manually on LD-74, then continue from the verification screen.
