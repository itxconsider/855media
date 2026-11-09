# Independent LDPlayer installation data

Implemented 2026-11-02 toward 3.2.5; the packaged version remains 3.2.4.

In **LD Options → Switch installation**, choose the actual `LDPlayer9` or
`LDPlayer14` folder containing LDConsole. Switching saves the current Page table
and settings, then loads the selected installation's own data. Identical device
indices, instance names, account UIDs and Page IDs do not select another
installation's files. The last selected installation is remembered on restart.
Switching is blocked during automation and account/editor save work.

Each canonical installation path has a stable SHA-256 directory under
`%APPDATA%\855Media\installations\<key>`. Console-file paths, path casing and
trailing separators use the same installation key. Separate installations of
the same LDPlayer version also have separate data.

The directory contains independent account mappings/removal records, Page rows
and media settings, protected credentials, protected mailbox settings, dispatch
queue, posting history/daily counters, ScreenTime history and automation settings.
Existing device routing and installation-specific leases remain in effect.
The accounts filter is now **All saved instances**, referring to the current
installation's stored records.

## First-use migration

- Prepare a staging directory and publish it by directory rename only after
  migration succeeds. Existing installation directories are never reimported,
  so removing an account or clearing a queue does not resurrect legacy data.
- Copy account mappings, Page rows, queue items and posting jobs whose saved
  binding matches an instance identity in the selected installation. Retain
  matching removal tombstones and their revision, protected credential/mailbox
  files, unknown posting outcomes, events and matching daily baselines.
- Legacy Page/queue/job rows without a binding belong only to the installation
  named by the original shared automation configuration. Legacy ScreenTime data
  lacks installation identity and is retained only with that original folder.
- Leave the original shared files unchanged. Unmatched/replaced/missing-instance
  records remain in those files; they are not guessed from a reused index/name.
  A successful Fetch in the appropriate installation can populate missing active
  accounts. This change does not certify a live Fetch or reconcile ambiguous
  historical ownership.
- Recover JSON from an existing backup if the primary is unreadable. If both
  cannot be read, stop migration without publishing a partial installation.

Normal application startup, including its injected ScreenTime service, uses the
installation stores. Explicit store/path injection remains available to isolated
diagnostics. Page/configuration saving requires a loaded installation. Switching
closes stale Page, credential, account-management and remapping editors, detaches
old queue persistence subscriptions and clears run-specific account bindings.

## Validation

- 78 focused Core checks passed: installation data/routing/account scope,
  dispatch queue, Fetch defaults/restoration, account selection/remapping and
  ScreenTime/posting isolation. The seven new migration checks cover separate
  versions/canonical paths, same-index records, preserved unknown outcomes and
  daily counters, repeat visits/deletions, failed migration, removal tombstones,
  backup recovery and protected-file ownership.
- Nine actual desktop installation-switch assertions passed with temporary
  data and the production ScreenTime service type: LD9 → LD14 → LD9 → LD14,
  separate settings/media/queues, stale-editor clearing, queue preservation and
  remembered active installation. Ten existing account-table scope assertions
  passed. The progressive Fetch diagnostic and its 17 existing scenarios passed.
- Main Debug build succeeded with zero warnings/errors after verifying the app
  was absent. No emulator action, production-data migration, publication or
  package was performed. Migration runs when the rebuilt app first loads each
  installation.

This related implementation and its regression coverage receive one release
credit: 26/10 → 27/10 toward 3.2.5. No packaging credit is added.
