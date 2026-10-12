# Retained and visible credentials — October 12, 2026

The credential editor previously opened blank and cleared its fields after Save.
An explicit instance remap discarded the old credential reference, requiring the
same account's password/TOTP secret to be entered again after recreating an LD.

The editor now loads the stored login identifier, complete `UID|Password|2FA|`
line and reusable factor type. The full line is visible as requested and stays
populated after saving. Closing clears the dialog's values; the encrypted record
remains. Saving retains the edited account's task selections. Loading is bounded
to its dialog instance so closing during a read cannot repopulate a closed dialog.
Credential values remain excluded from JSON configuration and application logs.

For an explicitly reviewed replacement of the same LD index, app package and
Facebook UID, remapping decrypts the existing credential with the old binding and
protects it for the new binding before committing. Login/rotation options remain
as selected when credentials exist. Device/access proofs and task selections are
reset; the new device still requires live personal UID verification. The previous
blob is removed only after the new mapping/reference commits. A read, save or
concurrent-edit failure leaves the original record in place and cleans up the
uncommitted copy. Moving to a different index, app or UID remains rejected.

Existing DPAPI records stay compatible; there is no plaintext migration or account
store format change. Credentials already deleted by an earlier remap cannot be
reconstructed; saving once on the current binding enables retention for future
reviewed replacements.

Validation: 748 focused PFlow/ScreenTime/AtxAgentClient tests passed, including real
Windows DPAPI retention across replacement bindings, full editor round trips,
wrong-account rejection and remap rollback. No real credential values were printed
or included in screenshots during verification.
