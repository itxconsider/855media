# LD-28 repeated Page name

Root preference record `logged_in_61594505558055` contains full name `Lemann+ Lemann+`, first name `Lemann+`, and delegate Page ID `1317839748079651`. Captured native switcher dumps show a single Page row named `Lemann+`. The fetch log on October 15 at 15:46:38 searched the doubled name; at 15:47:06 it failed to confirm selection and retained the unverified cached row. This is a name mismatch, not a duplicate Page entry.

The fetch recovery now considers a name consisting of two identical whole halves only after normal lookup fails. It requires a current native switcher containing a unique matching Page row for the shorter name and no matching row for the original full name. It retries the existing fetch with that name and requires live mapped Page/owner UID verification before saving the correction or accepting the recovered details. Graph-unavailable recovery requires a native Page UID lookup. Repetition alone never changes a name.

For an existing row matched by owner and Page/persona IDs, root refresh preserves its known name when the incoming root name is an exact doubled copy. The corrected name is also propagated to the pending UI lookup rather than reintroducing the bad name from the root snapshot.

Repaired the exact LD-28 Page row in `facebook_pages.json` and its associated Page record in `pflow_accounts.json` from `Lemann+ Lemann+` to `Lemann+`. Both originals are backed up beside them with suffix `.before-name-fix-20261015.bak`. Verified byte-for-byte text equivalence except for the intended name replacement. IDs, credentials, selection and access-verification fields were preserved; this repair does not mark the Page verified or publish anything.

Validation: 34 Page-name/live-UID tests passed, including the captured LD-28 switcher, missing switcher evidence, legitimate full repeated names, and root-refresh behavior. Release build passed with zero warnings/errors, and the existing `artifacts/startup-ready-fix` app output was updated after the user closed the running app. No live fetch or Page switch was performed during this repair.
