# Bloks Reel handoff and publication monitoring — 2026-10-25

LD-10's first `[386].mp4` attempt reached Reel Settings, then an empty native hierarchy caused Android sharing fallback. The fallback caption was verified and only its final Share was durably logged. The user observed two copies of the same video, one without a caption. An earlier unintended native tap is a plausible cause, not established by the available log.

## Native handoff

Retry empty accessibility observations three times, with cancellation. Resolve entry, album, media and editor controls from fresh observations. If Next has become Reel Settings, return that composer without tapping the old coordinates. Reject a visible publish action during editor navigation.

After attempting exact media selection, never open Android fallback. Recover a recognized composer only with this attempt's exact media evidence, matching job binding and unchanged staged bytes. Otherwise checkpoint Unknown/Unconfirmed and retain files for publication review, without fabricating a submission timestamp. Pre-selection failures may still take the existing verified Android chooser route. Caption/readiness/final durable Share checks remain.

## Publication monitoring

Keep the pre-submit success-message baseline fixed. Preserve fresh success exposed after Home refresh rather than absorbing it into the baseline. Once a submitted Reel's observed work cycle has cleared, Home is stable/refreshed and fresh Android notifications show no ongoing upload, permit one bounded early exact-profile post inspection. This helps when a pre-existing upload makes notification/cycle-only attribution ambiguous. Inspection eligibility is not confirmation; the existing author, full caption and recent publication evidence must pass. Pending UI, errors, unsettled screens and current ongoing uploads block inspection. The observed “Finishing reel upload. Keep Facebook open.” label is now explicitly pending.

## Validation

All 283 focused checks passed. Regression results are recorded in `artifacts/bloks-handoff/validated.trx`. New tests cover transient empty reads, recovery after retry exhaustion, replacement of Next at the Share coordinates, uncertain post-selection holds, clear/stale/ongoing/error inspection eligibility and the finishing-upload label. Existing caption, composer readiness, profile publication, confirmation, name/session and native Reel cases are included.

After the user closed the app, a fresh process check confirmed it absent. The main project Debug 3.1.9 build passed with zero warnings/errors; output is `855Media/bin/Debug/net10.0/855Media.exe`. This build adds no release credit. No live device action, Share, Graph request, job retry, mapping/history edit, process termination or package was performed. Actual Facebook publication remains untested by this change.
