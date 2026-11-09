# Main personal-profile name matching

The main personal-profile routes now use the same complete whitespace-insensitive name comparison as the supported native switcher. A display such as `DaraAbaRolSa` can match the saved `Dara Aba Rol Sa` without rewriting either name.

The change covers the active Menu header (including its profile suffix), own-profile title evidence, profile/date lookup name context, and complete actor labels used while checking the current profile/composer. The legacy personal-profile selector prefers a whole normalized-name match over an unrelated first row and rejects multiple matching row containers. Existing legacy fallback behavior remains unchanged when no exact candidate is available. The profile-header switcher also recognizes a whole whitespace-equivalent name before its existing navigation fallbacks.

Case and Unicode whitespace may differ. Punctuation, accents and character order remain significant. The Menu still requires one bounded active-profile header and excludes names inside Create Page/login actions. UID, owner, account binding, authenticated session, own-profile controls and contradiction checks remain required; names cannot authorize posting. This does not add support for the separate Dashboard-without-Edit-profile personal layout recorded on LD-64.

Eight added regression cases cover no/repeated/Unicode whitespace through the Menu/header/data/session pipeline, different complete names, preference over an unrelated first personal row, and duplicate normalized names. No live account navigation or post is performed by these tests. Main Debug output remains version 3.1.9; existing ZIPs are not replaced.

Validation on 2026-10-24: all 135 focused main-profile/navigation/native-scope/switcher/LD service checks passed with no failures or skips. Main Debug build passed with zero warnings/errors. See `artifacts/main-profile-name-tests.log` and `artifacts/main-profile-name-build.log`. This counts as one improvement, bringing accumulated credits to 11/10; a future verified 3.2.0 package consumes ten and carries one forward. Packaging remains pending.

Additional live validation on 2026-10-25: LD-56 switched through seven Pages and Marc Aurelius, passed own-profile/name and whitespace-variant checks, and restored Survival Outdoors. Zero app-issued Graph requests in this navigation-only diagnostic. See `docs/ld56-ui-only-switch-test-20261025.md`; no additional credit.
