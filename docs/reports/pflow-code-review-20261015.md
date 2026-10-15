# P-Flow defect review and fixes — 2026-10-15

Reviewed the current uncommitted P-Flow changes and their callers, with emphasis
on native account/UID navigation, Page switching and fetching, JSON Registry
loading, posting/Story confirmation, queue/history/media handling, and ScreenTime
browsing. Five actionable regressions were confirmed and fixed. Existing user
changes were preserved. No commits, device actions or public posts were made.

## Findings and fixes

### [P2] Match upload statuses rather than words in account names — PFlowStoryLayout.cs:13

`HasPendingUpload` searched every visible label for words such as `sharing`,
`retry`, or `processing`. An own-Story viewer for an account named `Sharing Tips`
or `Retry Plumbing` was therefore always treated as unfinished, preventing its
published Story from being confirmed. The check now evaluates text and
accessibility descriptions separately against complete status phrases.
Known upload, processing, retry and failure messages continue to block proof.
A shared nonbacktracking regex prevents expensive backtracking on long
punctuation followed by ordinary content. Eleven positive status cases and
four negative account/content cases cover the change.

### [P2] Preserve Facebook Page names containing Instagram — PFlowPersonaSwitcherService.PFlow.cs:623

The switcher excluded any row whose text contained `Instagram`, including a
Facebook Page named `Instagram Tips` or `Instagram`. This made legitimate targets
unselectable. Instagram identities are now excluded using their section boundary
or an explicit image badge, rather than the Page name. Tests retain the linked
Instagram exclusion, including when the section heading is absent.

### [P2] Recognize the final Facebook row above a switcher footer — PFlowPersonaSwitcherService.PFlow.cs:588

Native row extraction required two visible Facebook rows. Near the bottom of a
scrolled switcher, only the final Facebook Page can remain above the Instagram
or Accounts Center footer. Filtering out the Instagram rows then caused the
legitimate remaining Page to disappear from recognition. One visible Facebook
row is now accepted only with the existing supported sheet/list/identity evidence
and footer context. Its selection state is still read explicitly, and UID
verification remains independent.

### [P2] Use Registry controls throughout Page dashboard entry — LDPlayerService.cs:6057

The Registry could resolve a configured Dashboard alias, but Page fetching first
looked for hard-coded English dashboard labels. That preliminary check prevented
the JSON-defined control from ever being used. Dashboard refresh context also
ignored configured title aliases. Entry and refresh readiness now use the same
Registry control lookup; refresh header checks use the captured catalog's title
aliases. Tests verify the production entry helper, reject a wrong Page context,
and distinguish a configured loading header from a ready Dashboard.

### [P2] Exclude profile navigation tabs from birthday section boundaries — PFlowProfileDates.cs:18

Sticky `Posts` and `Friends` tabs, including their plain-text children, were
treated as content section headings. This could stop birthday scrolling before
the details appeared and reject a visible Born row below the tabs. Date reading
and the scroll-stop check now share a filtered set of content elements that
excludes explicit navigation tabs and their descendants. Actual Friends/post
sections still terminate the details region, preserving the rejection of dates
inside posts or suggestion cards.

## Data structures and algorithms

- Switcher extraction materializes container nodes once and determines the
  Instagram boundary once per container. It no longer rescans the entire
  container for that heading for every candidate row.
- Birthday boundary processing uses a set of tab descendants for membership
  checks, keeping content filtering consistent across both callers.
- Upload status recognition uses one reusable nonbacktracking regex, avoiding
  repeated broad substring matches and pathological punctuation backtracking.
- No performance benchmark was run; no numerical speedup is claimed.

## Verification

- Eight regression cases failed against their corresponding pre-fix code: six
  switcher/Story-name cases and two birthday-tab cases.
- First focused switcher/Story/JSON run: **97 passed**.
- Profile/link/Registry/Analytics checks: **105 passed**.
- Full LDPlayer suite: **1,543 passed**, zero failures.
- Final focused run after regex hardening and an additional Instagram-badge
  regression: **79 passed**, zero failures.
- Final application build: **0 warnings, 0 errors**.
- Build output:
  `C:/Users/itxco/AppData/Local/Temp/855Media-pflow-review-build/855Media.exe`.
- Logs are under `artifacts/registry-migration-parts/`: `review-regressions-before.txt`,
  `review-birthday-before.txt`, `review-focused.txt`, `review-profile-tests.txt`,
  `review-ldplayer-tests.txt`, `review-final-tests.txt`, and `review-app-build.txt`.

The reported defects are fixed and covered by saved XML or mocked-device tests.
Live Facebook navigation and publication were not exercised. Unseen layout and
language variants still require device evidence; this review does not establish
universal layout recognition or remove the existing identity/publication guards.
