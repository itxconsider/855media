# Data-driven Facebook layout registry — 2026-10-15

Implemented a hybrid registry: trusted C# layout templates retain structural,
identity, exclusion and action checks; validated UTF-8 JSON supplies versioned
variants, additional required/excluded labels, dashboard title and section
expansion aliases, and fallback Dashboard/Analytics navigation labels.

The shipped default JSON lists all 38 current templates. Startup copies it once
to `%APPDATA%/855Media/layouts/facebook-layouts.json`, preserving existing edits.
Settings > General > Facebook layout rules provides folder access, explicit
reload, load diagnostics, and a session-only C# fallback.

Reload installs a complete validated catalog atomically. Captured observations
retain their original catalog, and hierarchy match caches include the catalog
generation. Invalid JSON, unsupported properties or roles, invalid versions,
unknown templates, malformed UTF-8 and unreadable files retain the active valid
catalog. Fresh startup falls back to compiled rules if the user file is invalid.
Invalid files remain on disk for correction; last-good JSON is not persisted in
a separate recovery file.

Within one template family, the highest matching version wins; older versions
remain available when newer requirements do not match. Cross-family matches and
duplicate controls remain ambiguous. Navigation aliases do not override native
ambiguity or permit hidden/disabled controls. JSON cannot redefine Share/Publish,
UID verification, publication evidence, executable actions, or C# predicates.

AI-generated rules, AI execution, network rule downloads and automatic file
watching are deferred. New structural recognizers still require C# and fixture
tests. See [configuration and examples](../pflow-layout-registry.md).

## Verification

- Application build: zero warnings and errors, including compiled XAML bindings.
- LDPlayer test suite: 1,522 passed, zero failed.
- Final focused JSON/component/Analytics run after adding the contextual
  expansion-alias regression: 53 passed, zero failed (23 JSON cases).
- Coverage includes shipped-default parity, conditional version fallback,
  inherited target checks, native control ambiguity, hidden/disabled aliases,
  invalid packs, UTF-8 and missing files, cache invalidation, preservation of
  existing user files, old-observation consistency, concurrent reload/reset,
  and contextual section expansion.
- Packaged JSON confirmed present at `layouts/facebook-layouts.json` in the
  alternate build output.
- No live device fetch or public posting was performed.

Build output: `C:/Users/itxco/AppData/Local/Temp/855Media-data-registry-build/855Media.exe`.
Verification logs: `artifacts/registry-migration-parts/app-build.txt`,
`ldplayer-tests.txt`, and `json-final-tests.txt`.
