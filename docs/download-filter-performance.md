# Download filter responsiveness

The manager previously exposed a new lazy LINQ sequence for each status filter and raised FilteredDownloads changed on every added/removed row and status transition. Both the table and the hidden non-virtualized card view consumed it, causing repeated enumeration and control/image work. Active counts also scanned the full queue whenever read by a binding.

The manager now uses one stable read-only observable projection. UI-dispatched refreshes coalesce multiple queue/status changes into one pass. Filter switches and large imports use one Reset; ordinary status changes retain unaffected rows through incremental collection synchronization. Queued-to-started transitions do not remove/recreate active rows. Active counts are calculated during the same pass. Removed/cleared rows and disposed views detach their listeners.

Only the selected list/card presentation receives rows. In table mode, the hidden card view receives no source, avoiding thousands of hidden cards, thumbnails and their bindings. The visible table continues to use DataGrid virtualization. Card mode retains its existing wrap layout and still creates all matching cards; this change does not claim that visible card-mode rendering is virtualized.

The subscription guards, actual download queue, retry/cancel operations and output files are unchanged. The projection only observes the queue; it never starts jobs. Filter changes become visible on the next UI dispatcher pass. Visual design is unchanged.

Validation:

- Isolated desktop build: zero warnings/errors.
- `dotnet run --project tools/diagnostics/DownloadFilterCheck/DownloadFilterCheck.csproj`: 21 real view-model projection/headless DataGrid checks. Covers exact filters, retry ordering, moves/removals/clear, listener disposal, burst coalescing, selection retention and 10,000-row virtualization.
- Local projection-only timing: 80 filter switches over 10,000 rows in 12.4 ms. This excludes native window rendering, thumbnails and network I/O and is not a measured end-user latency claim.
- Headless DataGrid: nine realized rows for 10,000 items in the test viewport; All-filter completion bursts retained the same ItemsSource and selection.
- 170 downloading regressions and twelve download lifecycle checks passed.

Source/test build is at `artifacts/download-filter-build`. The running app and 3.1.6 release ZIP were not replaced; live user verification requires running the updated build. No beta packaged for this individual fix.
