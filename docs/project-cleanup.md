# Project cleanup — 2026-10-21

Twenty developer diagnostic directories were moved from `tools/` to `tools/diagnostics/`. Project references, linked sources/resources, Node fixture paths, benchmark baseline paths and documentation commands were adjusted for the extra directory level. Runtime helpers and the license generator retain their paths.

The solution now includes the existing Core test project, links the real README/tools guide and drops its missing License.txt reference. The root README explains the project layout instead of containing only a placeholder. Generated verification output is explicitly ignored.

The unused Real-ESRGAN tool/models/sample files, stale `.verify-build` output, tracked Python bytecode cache and two empty root logs were removed from the active tree through reversible moves into `artifacts/cleanup-archive-20261021/`. The archive contains an inventory recording 141 files and 222,613,811 bytes (about 212.3 MiB). These bytes remain on disk. Automatic approval review blocked the recursive deletion command; no permanent deletion or alternate deletion method was used.

Application/Core source changes already in progress were preserved. Release packages, release validation reports, local Python, the `.venv` junction, models, training data, licenses, keys and cookies were left in place. The archive is ignored and is not bundled in releases.

Validation passed: the four-project solution built with zero warnings/errors, all twenty moved projects' references/linked files exist, 194 focused downloading/layout tests passed and 48 moved collector/detail/WebView/lifecycle checks passed. The 3.1.6 ZIP SHA-256 remains unchanged. Live diagnostics were not run. No new beta was packaged for this cleanup.
