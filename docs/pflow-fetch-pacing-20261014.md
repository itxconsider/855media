# Additional fetch settling time

`PFlowFetchPacing` adds 500 ms to existing discovery delays. Examples:

| Existing wait | Fetch wait |
| --- | --- |
| 400 ms | 900 ms |
| 650 ms | 1,150 ms |
| 900 ms | 1,400 ms |
| 3,500 ms | 4,000 ms |

It is used for the fetch-specific native account navigator, Page and personal
details lookups, the details/transparency reader, discovery's configured waits,
profile navigation, and refresh settling. The Page lookup loop also waits
five seconds between Pages. Cancellation remains immediate; bounded readiness and
refresh checks still apply.

These waits help UI rendering settle. They are not a guarantee against Facebook
feature restrictions, security checkpoints, or account enforcement.

Validation: 163 focused pacing, refresh, native-account, hierarchy, profile-date
and Page navigation tests passed. No live fetching was run for this timing change.

Build: `C:\Users\itxco\AppData\Local\Temp\855Media-paced-fetch-build\855Media.exe`.
