# 855Media 3.2.1–3.2.3

Verified on 2026-10-28. These three self-contained Windows x64 releases use the same current source snapshot with separate assembly versions. They are not historical ten-fix subsets. Use 3.2.3 for the latest version.

Changes since 3.2.0 include native native Fetch and app-wide root removal; Page UID routing, public-link resolution, profile birthday reading and explicit restoration of removed account bindings; native Posting/ScreenTime name verification and bounded loading recovery; previous-upload settling, confirmed copyright-scan continuation and between-post pacing; badge-free switcher fallback and flexible own-profile/Menu title reading; caption suffixes, grouped photos and native photo composer repairs; independent file/group counts and name/date ordering; Dubbing QC and translation briefs.

All three Release publishes succeeded. Main Debug 3.2.3 built with zero warnings/errors after confirming the app was closed. Apphost, app and Core versions match each package. Each archive contains 341 files verified byte-for-byte against its publish folder. Runtime, Android helper, layout, source-hash, AMD64 and private-data checks passed. Bundled FFmpeg and yt-dlp match the prior verified package, whose archive remains unchanged.

The full Core suite was run once for this shared source: **3,462 passed, 17 failed, 3 skipped**. The 17 shared-switch/Reel/chooser failures remain unresolved; their production impact is not declared harmless. The latest focused name/switcher/Fetch/ScreenTime/publication regression run passed all 468 checks. Full failure names are in each package's VALIDATION.txt and the dist validation reports. No destination-PC or live publication validation was performed.

Final archives and SHA-256:

| Version | Final archive | SHA-256 |
|---|---|---|
| 3.2.1 | `dist/855Media-v3.2.1-release-win-x64.zip` | `78972d668157352b1ce92f2611e9a30b2826dd13d06593c65d76313bf6d4741e` |
| 3.2.2 | `dist/855Media-v3.2.2-release-win-x64.zip` | `ed9527e6090f98ff1d5f5cf25392bd806b831caec336522e8022f80a7ce687f1` |
| 3.2.3 | `dist/855Media-v3.2.3-release-win-x64.zip` | `eb4fcd3298bd2ad3224e479f29618b05fa9abbbcf01b4626304a884c2837632a` |

Preliminary archives without `release` in their filenames are superseded: their skipped-test wording was corrected in the final archives. They were retained after automatic approval review blocked the combined packaging-process stop and draft-cleanup command; the final archives were produced safely with distinct filenames.

Extract the complete final ZIP into a new writable folder. Configure LDPlayer separately. Machine-bound licenses remain specific to each computer; optional Python/model environments are separate. In-app update hosting and the proposed centralized account context are not implemented here. The unknown-count (-1) Reel album matcher issue remains unresolved.

Packaging consumed 30 of 36 credits, leaving **6/10 toward 3.2.4**; packaging adds no credit. No app was terminated, no live device or Facebook publication action was taken, no production data was edited and nothing was uploaded remotely.

Evidence: `artifacts/release-3.2.1-3-package/packages.json`, `versions.json`, `source-manifest.json`, per-version file manifests, and `artifacts/release-3.2.1-3-tests/full.trx`.
