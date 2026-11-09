# Foreground helper clipboard fallback

2026-11-05, Asia/Bangkok.

LD14-43 native Fetch stopped before copying Abigail's Page link because the ATX clipboard route was unavailable. UI navigation could continue through ADB, while clipboard preparation had no alternate backend. The local probe reproduced false preparation, a refused ATX connection, no agent process/forward and missing UIAutomator instrumentation.

The desktop still prefers the existing ATX marker-write/readback route. If it fails, it upgrades 885Helper when clipboard protocol1 is missing and uses a focused helper activity. This works with Android14's clipboard rules without changing the active IME, granting background clipboard privileges, enabling root, or installing UIAutomator packages. Existing helper network/status protocols remain compatible. Signed helper1.0.3/code4 is bundled.

The shell-protected provider arms a random session/request/operation. The DUMP-protected ClipboardActivity takes focus, performs the fixed prepare/read operation and finishes itself within2s. Session state expires after180s, individual requests after10s; all state stays in memory. Reads require successful preparation, emit only bounded Facebook URLs, and discard unrelated clipboard text. URL results are consumed from provider memory after host retrieval. No arbitrary clipboard history is returned or written to disk.

The host verifies Facebook foreground, safe non-input/non-authentication/non-pending UI and the same validated PID set before/after the helper operation. It waits for Facebook to resume and never presses Back on an unknown app or restarts Facebook. A per-installation/index gate and three-minute prepared route keep requests isolated; failures/cancellation invalidate helper proof. Reads stay on the prepared backend rather than silently changing routes after copying. The final existing supported-link resolver filter and native actor/copy freshness checks remain in force.

Android14's content-update command can return no row-count text on success. The bridge therefore verifies a fresh pending provider response with matching nonce/protocol/operation before launching the activity. An initial live probe exposed this and the final protocol tests cover the silent acknowledgement.

## Validation

All124 focused Core tests passed (15 new bridge cases), covering existing ATX clipboard behavior, helper health/network, native Fetch, request/operation/protocol/readiness/success matching, unsupported text, cancellation, foreground/input/process changes and invalidated proof. APK compiled, aligned, signed and signature-verified. Manifest protects both the provider and activity with DUMP; no broad clipboard permission was added.

LD14-43 production marker preparation passed through 885Helper. Actual Facebook Share Page→Copy link returned Abigail's fresh share URL, two helper reads agreed, and the public resolver returned numeric UID61596006604876. Graph0, published=false; original actor and keyboard restoration passed. This exercised the actual production bridge and resolver; it did not persist a Page row or certify a full metadata Fetch. The copy diagnostic predates only the final rejection-proof invalidation hardening; final helper marker compatibility and guard tests use the final Core.

An initially stopped LD9-4 was booted solely for compatibility validation. Android9 production marker write/read passed through the same fallback; it was then returned to its stopped state. No actor/IME change or LD9 Recents-flow modification was made. The existing successful ATX route is covered by its preserved HTTP regressions; no live LD9 Facebook link-copy/UID resolution was claimed.

Main Debug3.2.4 and diagnostic builds completed with zero warnings/errors after a fresh desktop absence check. Tested/main/final diagnostic Core SHA256 F3D58C08837EAC8DAA8C6DE6EE9CA5089873F1E76985416D882F216C9C05F005. Bundled APK SHA256 F070C33E5D37EC379D1FE0B0EF6AD9B34BB22240551CB93E803F6BCFFEF9638F matches main output. Evidence: artifacts/clipboard-helper/tests/validated.trx, ld14-43-prepared, ld9-4-prepared and ld14-43-share-copy/share-copy-result.json.

No production account/Page/history write, Graph call, publication, VPN change, device-ID change or desktop release ZIP was performed. Normal Fetch can persist the resolved Page through its existing identity/source/removal guards.
