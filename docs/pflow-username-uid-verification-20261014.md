# Username profile links and live UID verification

The LD-57 Profile Settings screen displayed a username link and a `Copy link`
button. The resolver previously accepted numeric profile URLs and share URLs
only; the native navigator recognized `Copy profile link` only.

The verifier now reads one supported link directly from `Your profile link` in
personal Profile Settings. Username URLs are resolved through public Facebook
redirects, numeric canonical URLs, or matching username canonical metadata with
an unambiguous `fb://profile/<UID>` app link. Conflicting IDs, Page app links,
unrelated feed/script IDs, and unsupported non-profile routes are not identity
proof. Public requests use no Facebook cookies or credentials.

The generic `Copy link` button is supported only within personal Profile Settings
with the `Your profile link` label. Existing clipboard reset/readback protection
remains in place. A visible link is checked again after resolving it to ensure the
settings surface did not change while the request was running.

## Validation

- 135 focused profile-link, native-account, and own-profile navigation tests passed.
- Live LD-57 `--inspect` succeeded and independently matched the mapped account UID.
- No logout, credential submission, publishing, or media transfer was performed.
- Diagnostic report: `tools/diagnostics/PFlowLoginCheck/bin/Debug/net10.0/reports/ld-57-20261013-194855`.
  The report name uses UTC; this verification occurred on October 14 in Bangkok.
- Updated build: `C:\Users\itxco\AppData\Local\Temp\855Media-username-uid-build\855Media.exe`.

If Facebook does not expose public numeric profile identity or denies the lookup,
verification still stops instead of substituting the saved mapping UID.
