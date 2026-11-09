# 855Media current-source build for a new PC — 2026-10-23

This Windows x64 self-contained build contains the current source, including changes made after the official 3.1.9 release. Its assembly version is 3.1.9.0 and its informational version starts with `3.1.9-current.20261023`. The separately named current-source ZIP preserves the original 3.1.9 archive. This requested transfer build does not advance the next official version (3.2.0) or consume the seven accumulated improvement credits.

Changes since the official archive:

- Dismiss the recognized Facebook Feed survey banner safely.
- Support the captured Page/personal Story cards and own-story toolbars; improve ScreenTime Story selection, playback detection and tray navigation.
- Bound Editor CPU filter threads per job.
- Apply sampled Smart Object Focus trajectories to export and preview, with improved timing, caching, smoothing and recovery.
- Preserve camera/custom metadata in MP4 exports, including split and merged output, titles and UTC timestamps.
- Add Samsung Galaxy S23, S23+, S23 Ultra, S24 and S24+ metadata presets alongside S24 Ultra.

## Start on the new PC

1. Extract the **entire ZIP** into a writable folder. Keep `tools`, `layouts` and all DLLs beside `855Media.exe`; do not copy only the EXE.
2. Run `855Media.exe`. The .NET runtime is included.
3. Activate access for the new computer in Settings → Subscription. Licenses remain machine-bound; this package contains no account data, cookies, settings or signing keys.
4. Install/configure LDPlayer separately and select its actual installation path in the app. Existing Android helpers and media tools are bundled. Configure Surfshark separately if your workflow uses it.
5. Point Reel/Photo/Story sources at paths that exist on the new PC, then begin with one LD and Fetch to verify the setup.

Optional Python environments and trained voices are separate. NVIDIA acceleration requires a compatible GPU/driver; supported jobs retain the existing fallback paths. Full live Story publication and operation on the destination PC remain unverified. The accompanying validation report records the build, tests, tool checks and archive verification, including any known test failures.
