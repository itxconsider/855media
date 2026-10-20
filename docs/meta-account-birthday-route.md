# Meta Account route and live LD-1/LD-2 validation

The new UI route begins at Settings & privacy, then Meta Account, then Your information and permissions. Birthday is already readable in its compact row. The optional Birthday detail screen is also recognized read-only; Edit, Confirm your age and birthday-visibility actions are not automated.

## Registry

Four C# templates and default JSON variants are registered:

- settings.meta-account-entry.v1: Settings & privacy with Your account, Meta Account, Subscriptions and Tools and resources. Only its observed Search settings edit field is permitted; it is never typed into. OpenMetaAccount is the navigation role.
- settings.meta-account-home.v1: Meta Account, Login and security, and Your information and permissions. PersonalInformation is the navigation role.
- settings.meta-information-permissions.v1: information screen with Birthday, Export your information, Access your information and Search history. No edit actions are exposed.
- settings.meta-birthday-details.v1: Birthday summary with Edit and Who can see your birthday. Supports both the age-confirmation/More options version and the simpler privacy-link version. No action controls are exposed; a date is read only from the compact summary row containing Edit.

These are explicit component matches, not Page identity evidence. Foreign apps, authentication, passwords and editable birthday forms remain blocked. Legacy Accounts Center/Personal details navigation is preserved. New information screens must match the registry before they can supply a date; old loose Birthday labels alone do not authenticate the new layout.

## Live inspection — 2026-10-21, LDPlayer 9

User explicitly requested inspection on LD-1 and LD-2 and scrolling every screen in this route. Both initially showed Facebook Feed. Navigation and scrolling were performed without changing account settings, posting, using Edit, opening age verification or changing birthday visibility.

Both instances were inspected through Settings & privacy from top to its Community Standards/About footer, Meta Account from top to bottom, Your information and permissions from top to Upload contacts, and Birthday detail with a scroll check. Both were returned to Facebook Feed afterward. Screenshots and redacted XML are under artifacts/meta-account-live-20261021/LD-1 and LD-2. Settings screenshots were skipped where the generic diagnostic input guard applies to its search field; redacted hierarchy captures cover those screens.

Observed shared information sections: Birthday, Export your information, Access your information, Search history, Activity from other businesses, External accounts, Upload contacts. Meta Account also exposes Login and security, Cross-profile experiences, Ad preferences, subscriptions/Meta One and Family Center. Those additional sections were observed through scrolling; their inner pages were not opened.

LD-1 Birthday includes More options and Confirm your age. LD-2 Birthday has the simpler Who can see your birthday link. The live registry matched the four LD-2 screens and extracted a date from both information and Birthday detail. All eight captured LD-1/LD-2 screens were then verified through portable registry/date tests. The manual live route and captured-screen date readers were exercised; the full fetch orchestrator was not run against live accounts.

## Fixtures and verification

Eight live fixtures are in docs/fixtures/pflow-layouts/ld{1,2}-meta-account-{settings,home,information,birthday}.xml. Account labels/emails and unrelated visible text are removed; real birthdates are replaced with a fixed fictional test date while preserving hierarchy/control structure.

57 focused tests passed across Meta Account capture recognition, navigation roles, date extraction, old birthday recovery and layout registry/catalog behavior. Tests also cover editable/foreign/password rejection and the UI fallback order. Desktop Debug build passed with zero warnings/errors into artifacts/meta-account-build-check. Logs: artifacts/meta-account-registry-regression-tests.log and artifacts/meta-account-build.log.

No beta was packaged and no running desktop executable was replaced or restarted. This is the fourth accumulated improvement toward 3.1.6.
