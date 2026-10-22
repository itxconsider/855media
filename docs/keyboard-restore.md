# Restore user input after automation

Posting/ScreenTime worker runs and both Fetch routes capture the current Android input method after boot and before automation starts selecting ADB Keyboard. The snapshot is scoped to the device operation and held under its existing device lease, across all accounts/jobs on that device.

On completion, failure or graceful Stop, a separate cleanup token with a twelve-second deadline restores the captured keyboard when it is still installed. If the operation began with ADB Keyboard already active or no usable snapshot, cleanup selects an enabled non-ADB input method, falling back to an installed one and enabling it when needed. Component names are validated before use; equivalent short/expanded Android component names compare consistently.

Cleanup confirms Android's default IME changed before force-stopping only `com.android.adbkeyboard`, then rechecks the selected IME. It never uninstalls the keyboard, clears app data, stops Facebook or kills the PC ADB server. A keyboard selected by the user during the run or during cleanup is preserved. No replacement, an unreadable/offline device or a rejected switch leaves ADB Keyboard unstopped and records `keyboard.restore-pending`; the worker status reports pending restoration. A successful change records `keyboard.restored`.

Restoration runs before optional LD shutdown and before releasing its device lease. Async disposal is an additional idempotent cleanup path; skipped work that never captures the keyboard makes no device changes. The next automation run retains existing ADB Keyboard installation/activation checks and can select it again for Unicode text input.

Validation: 292 focused keyboard/startup/session/ScreenTime tests passed, including thirteen new lifecycle cases for finish, cancelled-run cleanup, exception disposal, user changes, installed/disabled fallback, component normalization, no keyboard/rejected switch/offline failure, skipped work and untrusted command output. The isolated desktop build passed with zero warnings/errors. Logs: `artifacts/keyboard-restore-tests.log` and `artifacts/keyboard-restore-build.log`.

Test build: `artifacts/keyboard-restore-build/855Media.exe`. Live Android IME validation remains pending; no running emulator or existing app process was changed. This is one code-change credit toward 3.1.8, with no distribution package.
