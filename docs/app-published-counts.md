# Published counts in Page details

Published Reels and Published photos now show all-time confirmed publications made by this app from its posting history. They previously displayed discovered Facebook totals, which remained unknown for the screenshot's Page.

Totals are keyed by Facebook target ID and Page/profile identity. They include confirmed Reel/Video and Picture/Photo jobs, respectively, and exclude Story, failed, cancelled, queued, submitted and uncertain jobs. Duplicate job records count once. Device/owner reassignment, the calendar day, local folder contents and media cleanup do not reset them. Clearing or replacing the app's history database removes the evidence needed to compute these totals; they are not Facebook-wide counts.

The detail bindings use separate view-model properties and preserve existing discovered Facebook statistics. History is read once by the existing media-count refresh; the detail totals refresh when the target table opens, on Refresh counts, after posting and after History review. A readable empty history displays zero. A history read failure displays a dash with an explanatory tooltip instead of an invented zero.

Read-only inspection of the local history for Sisters Cooking found 4 confirmed Reels and 2 cancelled Reel jobs, so its details should display 4 Published Reels and 0 Published photos.

Validation: four focused count/availability tests passed; an isolated check of the actual desktop view model verified 4/0 text, property-change notifications, unknown-history behavior and preserved Facebook totals. Desktop build passed with zero warnings/errors at `artifacts/published-counts-build`. No live post was submitted and no new distribution package was created.
