# Keyboard activation across LD9 and LD14

2026-10-30. The shared activation routine now handles the observed LD9 SDK28 case where ime set acknowledges selection but leaves Pinyin as default. After exact installed-helper enumeration and the exact selection acknowledgment, it applies one settings fallback and requires fresh exact default-IME readback. Android14's existing unknown-method fallback remains available on SDK34+. Short and fully expanded helper component names are equivalent; other components remain rejected. Errors outside the supported observed cases cannot trigger the fallback.

Validation: 22 focused keyboard activation/session checks passed (artifacts/keyboard-activation-fix/validated.trx). Live LD9 index1 and LD14 index45 both passed APK installation, installed detection, activation and active-default verification, followed by successful normal-keyboard restoration. LD56 excluded. No caption delivery or publication was performed in these activation tests. Android14 caption delivery was validated separately in the earlier photo dry run.

Main Debug 3.2.4 built with zero warnings/errors after explicit app closure and fresh process-absence verification. No package produced. Diagnostic build also passed. Evidence: artifacts/keyboard-activation-fix/ld9/keyboard.json and ld14/keyboard.json.
