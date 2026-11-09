# LD-20 native Fetch live diagnostic — 2026-10-27

User-authorized production Core Fetch test, 12:06:44–12:07:50 Bangkok time. Device lease, pending-publication and ongoing-upload guards passed. Diagnostic reads saved mappings but never persists results or edits production account/queue/history stores. Only LD-20 was targeted. No root or publication action; managed Graph request audit: zero.

The complete initial switcher scan returned selected Page `Caprani Plumbing+` and personal profile `William Rhodes`. Selecting William passed fresh native confirmation in 10.9 seconds. Own-profile navigation opened Profile Settings, but the numeric-link reader returned null in 6.2 seconds. The captured viewport has no profile link; the reader polls this viewport without scrolling. This does not establish that the complete settings page lacks a link.

Fetch then entered its personal-restoration finally block. Selection returned false after 45.3 seconds and replaced the unresolved-ID outcome with `Native Fetch could not restore the personal account; Facebook was left open.` The final screenshot actually shows William selected in an alternate Profile Settings actor sheet, with Caprani Plumbing+ and Create new profile or Page. Thus the reported restoration failure is a UI proof/navigation failure; it is not evidence that the actor changed away from William.

Keyboard restoration passed. After capturing the final state, one Back command dismissed the diagnostic-owned actor sheet. Facebook remains open on personal Profile Settings. Page detail extraction was not reached. No production fix, main build, release credit or package.

Evidence: `artifacts/ld20-native-fetch-20261027/native-fetch.log`, step XML/PNG captures and `native-fetch-final.png`. Diagnostic-only `PageSwitchLiveCheck --ld=20 --native-fetch --output=artifacts/ld20-native-fetch-20261027` uses the production native Fetch service with per-operation observations. Diagnostic/Core build passed with zero warnings/errors; the running main desktop app was not rebuilt or terminated.
