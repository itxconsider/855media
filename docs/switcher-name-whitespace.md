# Complete-name matching across whitespace differences

Completed 2026-10-24; one improvement credit toward 3.2.0.

Supported native switcher rows now compare complete names using a shared whitespace-insensitive, ordinal case-insensitive key. For example, `Dara Aba Rol Sa`, `Dara   Aba Rol Sa`, tab/newline/nonbreaking-space variants and `DaraAbaRolSa` compare equally. The same comparison corroborates Graph-bound profile names and detects ambiguous saved mappings. Displayed/stored names are not rewritten.

Only Unicode whitespace is removed for comparison. Letter order, punctuation, accents and all other characters remain significant. Empty names, partial names, prefixes, suffixes and multiple matching Facebook rows are rejected. Instagram exclusion, row usability/selection, target type rules and fresh UID/owner/process/session verification remain in place. Name matching does not establish account ownership. No fuzzy/edit-distance matching or substring regular expression was added; legacy navigation fallback behavior is unchanged.

The user's [string-matching reference](https://stackoverflow.com/questions/2566322/string-matching) motivated the whitespace variants; the implementation uses a complete normalized-key equality rather than its unanchored example patterns.

Validation: 14 new whitespace/negative/ambiguity checks and 88 total focused switcher/discovery/name/native-identity-scope regressions passed, zero failures/skips. Main Debug build version 3.1.9 passed with zero warnings/errors. Logs: `artifacts/switcher-whitespace-tests.log`, `artifacts/switcher-whitespace-build.log`. No live Dara account switch or publication performed. No new ZIP produced; counter reaches 10/10 toward 3.2.0, pending packaging.
