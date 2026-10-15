# Live personal-account UID verification

The native verifier now recognizes repeated Edit profile controls and both
Edit cover photo accessibility labels. Recognition does not choose an ambiguous
button or establish the account UID.

Verification returns from known personal About/transparency screens, dismisses
the specific link-sharing update notice, and reads the personal profile link.
When the embedded Profile tab omits settings, it can open the mapped numeric
profile through the existing Facebook deep-link method. That UID is only a
navigation destination: the fresh own-profile settings link must independently
identify the live account, and the session service still checks it against the
binding. A profile transparency Page ID is never used as the personal UID.

## Validation

- 121 focused native account, own-profile navigation, and profile date tests passed.
- Application build succeeded with zero warnings and errors.
- Live `PFlowLoginCheck --inspect` succeeded on LD-1, LD-2, LD-3, and LD-4.
- The checks performed no publishing, credential submission, or logout.
- LD-2 required the full-profile deep-link fallback because its embedded tab
  exposed editing controls but no settings control.

Successful diagnostic reports are beneath
`tools/PFlowLoginCheck/bin/Debug/net10.0/reports/`:

| Instance | Report |
| --- | --- |
| LD-1 | `ld-1-20261013-110721` |
| LD-2 | `ld-2-20261013-110915` |
| LD-3 | `ld-3-20261013-110119` |
| LD-4 | `ld-4-20261013-105622` |

Updated executable:
`C:\Users\itxco\AppData\Local\Temp\855Media-uid-check-build\855Media.exe`.
