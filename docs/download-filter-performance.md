# Download filter responsiveness

The manager previously exposed a new lazy LINQ sequence for each status filter and raised FilteredDownloads changed on every added/removed row and status transition. Both the table and the hidden non-virtualized card view consumed it, causing repeated enumeration and control/image work. Active counts also scanned the full queue whenever read by a binding.

The manager now uses one stable read-only observable projection. UI-dispatched refreshes coalesce multiple queue/status changes into one pass. Filter switches and large imports use one Reset; ordinary status changes retain unaffected rows through incremental collection synchronization. Queued-to-started transitions do not remove/recreate active rows. Active counts are calculated during the same pass. Removed/cleared rows and disposed views detach their listeners.

Only the selected list/card presentation receives rows. In table mode, the hidden card view receives no source, avoiding thousands of hidden cards, thumbnails and their bindings. The visible table continues to use DataGrid virtualization. The initial fix still created all visible-mode cards. The 2026-11-04 follow-up below replaces that remaining path with virtualized card rows.

The subscription guards, actual download queue, retry/cancel operations and output files are unchanged. The projection only observes the queue; it never starts jobs. Filter changes become visible on the next UI dispatcher pass. Visual design is unchanged.

Validation:

- Isolated desktop build: zero warnings/errors.
- `dotnet run --project tools/diagnostics/DownloadFilterCheck/DownloadFilterCheck.csproj`: 21 real view-model projection/headless DataGrid checks. Covers exact filters, retry ordering, moves/removals/clear, listener disposal, burst coalescing, selection retention and 10,000-row virtualization.
- Local projection-only timing: 80 filter switches over 10,000 rows in 12.4 ms. This excludes native window rendering, thumbnails and network I/O and is not a measured end-user latency claim.
- Headless DataGrid: nine realized rows for 10,000 items in the test viewport; All-filter completion bursts retained the same ItemsSource and selection.
- 170 downloading regressions and twelve download lifecycle checks passed.

Source/test build is at `artifacts/download-filter-build`. The running app and 3.1.6 release ZIP were not replaced; live user verification requires running the updated build. No beta packaged for this individual fix.

## 2026-11-04: large-queue card filtering

The remaining card view used a ScrollViewer containing an ItemsControl/WrapPanel.
Every matching download built its thumbnail, progress indicators, status styles and
buttons on the UI thread. Filtering thousands of matches recreated that full tree.
The earlier projection-only and DataGrid checks did not cover this visible-card path.

The manager now uses DownloadCardGrid, a ListBox with a VirtualizingStackPanel over
small horizontal card rows. It groups the existing filtered download objects into
four-to-eight-column rows and only realizes viewport rows. It preserves the original
card template and commands, queue order, filters and complete scrollable item set;
there is no result cap. Collection bursts coalesce into one grouping refresh, unchanged
rows retain identity, and view detachment removes the source listener.

The filter projection also detects large equal-count replacements/reorders, rather
than looking only at the net row count. It emits one Reset for large changes and keeps
incremental notifications for ordinary completions/removals so unaffected table
selection survives. No download is started, retried, canceled or removed by filtering.

Validation:

- Isolated desktop build passed with zero warnings/errors. After user closure and
  fresh process-absence verification, main Debug3.2.4 also built with zero warnings/errors.
- All 46 real projection/headless UI checks passed using 10,000 download view models.
  Card view initially built 12 controls, versus 10,000 potential cards; DataGrid realized
  nine rows. Twelve repeated filter/layout switches kept viewport realization bounded.
  These are synthetic headless results, not live Windows responsiveness measurements.
- Reached the final download, regrouped to eight columns, cleared/restored the card
  source and detached/reattached after queue mutation. The actual production card
  template rendered bounded file labels and retained its original row commands.
- A 1,200-row equal-count replacement generated one Reset. A single completion in
  the large Active table generated one removal and retained the unaffected selection.
- All 215 focused download/collection Core regressions and twelve real view-model
  cancellation/retry lifecycle checks passed.

Evidence: artifacts/download-card-build/filter-check.txt, lifecycle-check.txt and
 download-regressions.trx. No live download, thumbnail-network test or package was
performed. The fix requires the rebuilt app; the already running process uses its
previous code. Existing release ZIPs are unchanged.