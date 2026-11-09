# Fetch settings

Run settings → Posting & devices → Fetch contains Get Pages by API, Skip verified accounts and Skip verified Pages. All default off and persist through AutoPostConfig export/apply. The API checkbox moved from the Targets toolbar and retains its existing optional Fetch behavior.

Skip verified accounts keeps the verified primary profile details while still running native owner verification, actor discovery and Page fetching. Newly discovered or incomplete Pages are collected even when this option is on. It never skips the whole account. This is a saved-data optimization, not fresh posting identity proof.

Skip verified Pages preserves completed, verified, owner-bound saved Pages while fetching the others. Skipped IDs travel separately from newly collected details; reconciliation preserves their existing objects, metadata, sources and verification state. Native actor discovery still runs for processed accounts. Explicit single-Page Fetch always refreshes. Turn the skip options off to refresh all data.

Validation: all99 focused Core checks passed, including native Fetch default/skip/explicit-refresh cases and config JSON round trip. Two actual desktop checks passed: VM export/apply of allthree settings, and table reconciliation retaining the skipped Page object, verification, media source and metrics with zero device commands. Main Debug3.2.4 built with zero warnings/errors and matches tested Core. No production settings/data edit, Graph, publication or release ZIP. Full live Fetch with the skip options was not run.

Correction validation (2026-11-06): 60 focused Core checks passed, including two profile-details-skip cases proving missing Page discovery with Page skipping enabled or disabled. Main Debug3.2.4 built with zero warnings/errors. Desktop reconciliation checks also cover retained profile date/email/media. No live Fetch or production data changes.
