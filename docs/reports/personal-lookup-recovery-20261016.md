# Personal data lookup recovery — 2026-10-16

The personal-account fetch now recovers hidden navigation and reads birthdays from both supported Accounts Center layouts. It retains the join date when a birthday is unavailable and returns to the verified own profile after lookup.

## Changes

- Exclude duplicated profile labels inside Create Page/account actions when resolving the active drawer profile.
- Reveal a scrolled drawer header and hidden native navigation using bounded gestures and fresh UI observations.
- Recover an ignored Accounts Center deep link through Menu, Settings and privacy, Settings, and Accounts Center. Recognize accessibility ordinal labels and passive list wrappers without accepting disabled controls.
- Support the newer Profiles and personal details landing page. Read the Birthday field in its Personal details section; opening the profile card would lead to name/avatar editing instead.
- Require explicit date-field context, reject ambiguous dates and editable fields, and restore the own profile after reading. Lookup does not save personal-data edits.

## Verification

- Live production lookup on LD-67: identity switch confirmed, birthday found, join date found, own profile restored. Final read at 21:53:29 +07; restoration confirmed at 21:53:33 +07. No personal field values are reproduced here.
- Final focused regression suite: 171 passed, zero failures.
- Broader LDPlayer suite: 1,720 passed before the final reader-only layout refinements; those refinements are covered by the final focused suite.
- Desktop build: succeeded, zero warnings and errors.

Live observations were read-only navigation; no posts were published or personal-data changes saved. Other device layouts remain subject to their available fields and supported UI evidence.
