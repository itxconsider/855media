# LD-19 Page identity correction

The screenshot showed Caprani Plumbing & Heating open on LD-19, while bulk deletion incorrectly reported another actor.

Read-only live checks found:

| Identity | ID |
| --- | --- |
| Underlying mapped account | 61594582829499 |
| Current Graph `/me` Page persona | 61594688834095 |
| Canonical Page ID | 1407114619142015 |

The live device's `logged_in_61594688834095` record explicitly delegates to Page `1407114619142015`. The current native token returned the Page's published posts successfully. A separately requested Page access token returned HTTP 403/code 200 for reads; the implementation retains the working native token rather than assuming a replacement token has the same permissions.

Fixed the check to accept the distinct persona only when its saved UID, fresh device delegation and canonical Graph Page node all match. Post author and post ID validation continue to use the canonical Page ID. The mapping is checked again before every deletion; the UI also rejects changes to the saved persona while the window is open.

The production preview successfully returned all **21 posts**, matching the screenshot's count. **No DELETE requests were sent.** Deletion permissions remain untested on live posts.

Validation: 28 focused tests passed, including mismatched persona, delegation and canonical node, plus delegation changing after preview. Desktop build produced `artifacts/bulk-delete-fix/855Media.exe`.
