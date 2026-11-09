# Camera metadata export fix and finished-file repair

Completed 2026-10-23. This follows the read-only audit in `artifacts/metadata-audit/report.md`.

## Exporter changes

`CameraMetadataSettings.BuildOutputMetadataArgs` combines tag generation with MP4/M4V/MOV muxer options (`+faststart+use_metadata_tags`). Camera make/model/software/lens and custom keys therefore survive actual MP4 muxing. Normal rendering, GPU/CPU fallback, stream-copy exports and split concatenation use the same helper.

Copy Original Metadata maps input 0, including when isolated audio occupies input 1. Camera presets no longer claim a fabricated camera encoder; FFmpeg/NVENC can retain their actual encoder identity. Handler labels target the video stream. Samsung's existing 24mm/f1.7 lens description is also written as separate descriptive focal/aperture tags. The explicit timestamp choice applies to Clean Normalized as well as camera profiles and is written as invariant UTC text; Copy Original retains the original metadata instead.

Normal exports use the output basename for the title. Split children carry an internal, nonserialized final-part title rather than temporary slice names, and merged output reapplies the master profile/title. Checkpoint versioning includes the title and prevents reuse of old metadata output stages.

These are descriptive metadata profiles. They do not recreate native camera EXIF/proprietary structures or change the capture provenance or codec of a video. Windows Properties may expose fewer fields than FFprobe; native property-display validation is pending.

## Existing videos repaired

All 224 audited final MP4s in `C:\Users\itxco\Videos\pt` (about 90.04 GiB) were rewritten in place using stream copy with the selected Samsung Galaxy S24 Ultra profile:

- Samsung / SM-S928B / Android 14.
- Galaxy S24 Ultra Main Camera 24mm f/1.7, plus focal_length=24mm and f_number=1.7.
- Titles match their final basenames, including Part numbers.
- Original container and stream creation times were retained in UTC. Existing filesystem creation and last-write times were restored.
- Actual encoder identification, codec, dimensions, pixel format, frame rate, audio properties and durations were preserved.

The repair helper (`tools/diagnostics/RepairEditorMetadata.ps1`, PowerShell 7.6/.NET 10) accepts an explicit audit manifest restricted to one folder, rejects reparse points and unfinished candidates, denies concurrent writers, writes a sibling candidate, probes it and compares compressed A/V stream hashes before atomic replacement. A short-lived rollback copy is removed only after replacement verification. A journal makes completed version-2 repairs resumable; two disjoint manifest halves were processed with separate journals.

Final independent verification: **224 metadata passes, 224 verified replacements, 224 original audio/video hash matches, zero hash failures, zero timestamp failures**. Earliest pre-repair hashes were compared to final hashes, including files processed during the initial helper pass. Evidence is `artifacts/metadata-repair/final-summary.json`, `final-probes.json` and the two `journal.jsonl` files.

## Recovery notes

The initial helper pass allowed PowerShell to parse JSON UTC timestamps as DateTime and stringify them as date text. Follow-up verification caught the error. The helper now uses `ConvertFrom-Json -DateKind String`; every affected file was repaired again using the original pre-repair snapshot for container/stream creation times. Final checks confirm restoration of every timestamp.

Two source AAC sample-count headers included an empty sample not emitted by the demuxer. Actual packet counts and compressed stream hashes matched after remuxing; the helper verifies real packet counts when that header estimate changes. No media payload was changed.

Automatic approval review rejected deletion of the failed candidate `C:\Users\itxco\Videos\pt\[027]_Part2_original1x.mp4.metadata-rewrite.tmp.mp4` with “blocked by policy.” It was left untouched, about 296 MB (283 MiB); corrected repairs used a new candidate name. No rollback copies or version-2 candidates remain. The retained file is outside the 224-file finished-video manifest.

## Code validation and release status

All 187 Editor tests passed with one existing speech-isolation test skipped. Eleven focused metadata checks passed, including actual MP4 profile/custom-field round-trips, source-tag stripping, original-input selection with a second audio input, rendered split/merged metadata and final titles. The main Debug full-solution build passed with zero warnings/errors. Main executable: `855Media/bin/Debug/net10.0/855Media.exe`. The app was not restarted and the packaged 3.1.9 ZIP was not changed.

This completed fix and its existing-file remediation count as one improvement credit: **6/10** toward 3.2.0.

## Samsung S23/S24 series presets

The Editor metadata list now includes Galaxy S23 (SM-S911B), S23+ (SM-S916B), S23 Ultra (SM-S918B), S24 (SM-S921B), S24+ (SM-S926B), and the existing S24 Ultra (SM-S928B). New enum values are appended: the legacy SamsungGalaxyS24 value still means S24 Ultra and Custom retains its original value.

S23 defaults use Android 13, S24 defaults Android 14; these are descriptive preset defaults, not detection of the source device or its current OS. Base/Plus main-camera aperture defaults are f/1.8 and Ultra f/1.7. New presets leave focal length unspecified unless supplied. User model/software/lens/optical overrides remain available.

Model identifiers and apertures were checked against Samsung's [S23 comparison](https://www.samsung.com/fr/support/mobile-devices/comparatif-galaxy-s23-ultra-s23-plus-et-s23/) and [S24 comparison](https://www.samsung.com/de/support/mobile-devices/vergleich-galaxy-s24-ultra-galaxy-s24-plus-galaxy-s24/).

Validation: 16 focused metadata tests passed, including all-profile actual MP4 export round-trips, six Samsung model/override cases and saved enum compatibility. Full Debug solution build passed with zero warnings/errors. Existing exported files were not rewritten by this change.
