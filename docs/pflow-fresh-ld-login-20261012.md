# Fresh LD-1 login — October 12, 2026

The fresh LD-1 had Facebook and ATX available but no `com.android.adbkeyboard`
package. Native login stopped before entering credentials because Android text
input could not be verified. The bundled ADBKeyboard APK was installed on LD-1,
enabled, and selected; both the secure default-input setting and the bound input
method reported `com.android.adbkeyboard/.AdbIME`.

The login adapter now provisions and verifies a missing keyboard before secret
input. It checks the assigned device binding before installation/activation and
stops without entering credentials if any prerequisite fails.

Automatic recovery then submitted one password and one TOTP code. Facebook accepted
the code and showed its first-login Location Services introduction. The original
verification stopped there without starting tasks. At the user's instruction, the
new onboarding handler continued that screen and handled Facebook's location
permission. LD-1 subsequently reported coarse/fine location granted. A fresh own
Profile Settings link inspection verified Carey Northcutt's live UID
`61586742627455`. No password or TOTP was repeated to finish onboarding.

The user's preference is to decline credential-saving prompts in Facebook and
Google. Recognized Facebook save-login dialogs use Not now; recognized Google
credential-manager dialogs use Close. The location handler also declines a save
dialog appearing after that onboarding step. Session/device identity proof is
still required; optional setup never counts as authenticated UID evidence.

Validation: 741 focused PFlow/ScreenTime/AtxAgentClient tests passed, zero failures
or skips. The normal Debug solution build passed with zero warnings/errors. The
idle desktop app was closed normally and relaunched with the new handlers.
No posting or ScreenTime public interactions ran during this login verification.
