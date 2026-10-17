# Bulk Page post deletion

In **Targets & media**, check exactly one Page on FB-01, then choose **Review / bulk delete posts**. Switch its mapped LD to that Page before loading.

1. Load all published Page posts. Graph `/me` must match the saved Page ID or its verified New Page Experience persona UID, and the device's underlying owner must match the saved account UID. For a distinct persona UID, require the exact saved persona, fresh device `delegate_page_id` mapping, and a matching canonical Graph Page node. Names alone never establish this link.
2. Review the complete paginated list, then select individual posts or **Select all**.
3. Type the verified numeric Page ID and click **Permanently delete selected posts**.

This permanently deletes the selected posts returned by `/PAGE_ID/published_posts`. It does not claim to enumerate separate Reels, Stories, scheduled drafts or media libraries, and does not delete the Page itself. Personal profiles and app clones are unsupported. A token able to read `/me` is not necessarily authorized to list or delete posts. Permission failures stop the workflow; there is no automatic UI deletion fallback.

The service rechecks the live Page actor and each post's author immediately before its DELETE request. Only IDs from the completed preview can be selected. Each request has a durable, credential-free audit entry before sending and a terminal outcome afterward. Token material is sent only in a Bearer header, never in audit files or request URLs. Pagination uses cursors and never follows Graph's arbitrary `next` URLs.

Reports are JSONL files under `%APPDATA%/855Media/page-post-deletions/`; the exact path and individual results appear in the window. `Deleted` means Graph confirmed success; `Rejected` means it denied deletion; `Unknown`, or a `Requested` entry without a terminal outcome, requires checking the post on Facebook before retrying. Processing stops on the first failure or cancellation, leaving later posts unattempted. Reload and review again before a new deletion attempt.

Validation: focused mocked HTTP/device tests and desktop build; no live posts deleted during implementation. Meta's official SDK exposes [published Page posts](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/page.py) and [PagePost DELETE](https://github.com/facebook/facebook-python-business-sdk/blob/main/facebook_business/adobjects/pagepost.py).
