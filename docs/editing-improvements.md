# Upscaling improvements — 2026-10-19

These eight upscaling improvements are included in 3.1.1, alongside two account-fetch fixes. See [release notes](release-3.1.1.md).

| Area | Previous behavior | New behavior |
| --- | --- | --- |
| Interrupted AI work | Finding any extracted JPEG skipped extraction. AI and face output counts alone could qualify a cache as complete. An interrupted extraction could therefore produce a shortened video on retry. | Each stage records completion only after its process succeeds and its nonempty, sequential frame set is verified. Resume checks the record against source/settings and file names, sizes, and modification times. Interrupted, missing, changed, or uncertified caches rebuild the affected stage and dependent frames. Completed stages can still be reused. |
| Split recovery | Any upscaled clip over 1 KB was treated as finished; any existing input slice was reused. Canceled encodes and interrupted slices could be merged as valid results. | Input slices and upscaled clips receive separate completion records after success. Resume reuses only matching certified files and regenerates incomplete work. Completed clips retain their frame counts for resumed progress. |
| Framerate and timing | AI muxing interpreted the existing image sequence at the requested target FPS. A 30 FPS, 10-second clip selected for 60 FPS could play in 5 seconds while the original audio remained 10 seconds. Field-rate deinterlacing also doubled extracted frames without a matching sequence rate. | Extraction uses a known constant cadence; field-rate deinterlacing uses twice the source rate. Muxing reads frames at that cadence multiplied by playback speed, then independently converts to the selected output FPS by duplicating/dropping frames. The same example remains 10 seconds at 60 FPS. Selecting 2× speed makes video and audio approximately 5 seconds; selecting 0.5× makes both approximately 20 seconds. FPS conversion does not add motion interpolation. |
| Queue failures | Missing inputs or other errors before the service's processing handler could exhaust retries while leaving status Processing. IsRunning and batch completion could remain stuck. Manual retry retained the exhausted retry count. | The queue explicitly sets Failed and supplies an error before the failure event and persistence. Later jobs can continue. Manual retry clears the old error and resets the two-retry allowance. Canceled attempts do not enter soft-error retry or mux fallback. |
| Split settings and progress | Children omitted the selected target framerate and inherited half the master frame estimate regardless of split count. Multiplying a child's frame count by the number of parts misrepresented uneven splits. Progress could reach 100% before merge finished. | Children inherit target FPS and use their own segment duration for initial frame estimates. Each slice is probed; master totals sum the individual estimates/actual extracted counts. Progress is weighted by segment duration, counts completed cached parts, and stays below 100% until final output processing succeeds. |
| Native video file size | Native rendering used unrestricted quality settings (CRF, CQ, QSV global quality, or AMF constant QP), unrelated to the compressed source bitrate. Faster playback could still produce larger files. | Native rendering probes the first video stream's bitrate and uses it as the target and maximum video rate with a two-second encoder buffer. NVENC, QSV, AMF, CPU encoders, and CPU fallback use the same budget. When stream bitrate is absent, the budget is estimated from input size/duration after reserving audio and container overhead. Unknown bitrate is explicitly logged before retaining encoder defaults. |
| Native audio file size | Audio filters and CPU fallback could re-encode audio at 320 kb/s even when the source used 128 kb/s. | Unchanged audio remains copied. Native audio encoding is limited to the source stream rate (the lowest known rate for multiple audio streams), up to 320 kb/s; unknown audio rate uses 128 kb/s. |
| Unchanged exports | Original1x with unchanged media was still decoded and encoded, potentially increasing size and introducing generation loss. | Original-size jobs with matching codec, original aspect ratio/FPS, 1× playback, original audio, and no active effects use stream copy. This applies to split children as well. Requested crop, speed, color, clarity, denoising, deinterlacing, face restoration, codec, or audio changes continue to render. Metadata can still be normalized during remuxing. |

Old caches without completion records rebuild once because their completeness cannot be established. Checkpoint metadata is stored beside frame directories so neural tools receive image files only. Source identity uses the full path, file size, and modification time; artifact validation likewise uses file metadata, not a full media decode or content hash.

Lossless stream-copy split boundaries remain keyframe/packet dependent. Probed segment durations can differ slightly from requested durations; the updated totals follow those actual durations. New target-FPS behavior is covered for both AI muxing and split native rendering.

Source-based rate control is a bitrate policy, not an exact byte-size guarantee. Around 2× playback, file size should be approximately half at similar bitrate; encoder buffering and container overhead affect short clips. More complex effects, higher output FPS, or larger resolution must now fit the source budget in native mode, which can reduce visual quality. AI frame-pipeline encoding retains its quality-oriented defaults. Active default Facial Clarity still requires rendering; stream copy only applies when it and all other media changes are disabled. Split checkpoint keys were versioned so completed clips from the previous unbounded encoding policy are rebuilt instead of reused.

## File-size verification on the supplied videos

Read-only inspection of `D:\Bushcraftimprovisationvideos` confirmed that video [34] changed from landscape 1920×1080 at approximately 1.615 Mb/s to portrait 1080×1920 at 6.921–7.301 Mb/s. Its two rendered parts totalled 506.7 MB versus the 241.0 MB original, despite their combined duration being approximately half. Outputs identify NVIDIA H.264 encoding and higher audio rates, confirming rendering after splitting.

A 30-second stream-copy excerpt of that source was created under the repository's ignored `artifacts/upscaling-size-check/` directory. The original user media was not modified. With 2× playback, portrait conversion, and Facial Clarity enabled:

| Export | Size (decimal MB) | Relative to the input excerpt |
| --- | ---: | ---: |
| Input excerpt | 10.15 | 100% |
| Fixed CPU H.264 output | 4.76 | 46.9% |
| Fixed NVIDIA H.264 output | 5.09 | 50.1% |

The NVIDIA run used the actual GPU with no CPU fallback. Video target/peak was 2.570 Mb/s for this excerpt; measured NVIDIA output video rate was 2.559 Mb/s, and output audio was 127 kb/s. Source bitrate varies by segment, so the excerpt's bitrate differs from the full original's average. Full logs and machine-readable measurements are in `artifacts/upscaling-size-check/user34-fixed-export-results.json` and the adjacent export logs.

## Verification

- Full solution build: passed with 0 warnings and 0 errors.
- Upscaling tests: 165 passed, 1 existing manual test skipped.
- Complete Core suite: 2,611 passed, 1 existing manual test skipped, 0 failed.

Regression coverage includes interrupted extraction with stale frames, invalidated frame sets, changed source/settings, partial split inputs/outputs, canceled split resumption that reuses completed work, queue failures and manual retries, and child settings/estimates for unequal segments.

File-size regression coverage checks source stream bitrate parsing, fallback budget estimation, removal of unrestricted quality modes across CPU/NVENC/QSV/AMF argument sets, unchanged stream-copy eligibility, exact compressed-stream hashes after remuxing, split-only aggregate size, and reduced file size with synchronized audio/video at 2× speed both with and without portrait/clarity effects. CPU and NVIDIA H.264 encoding were also exercised on a user-source excerpt; Intel/AMD hardware encoding was not available for equivalent runtime validation.

Real FFmpeg integration tests generate video and audio fixtures and measure decoded output durations and frame counts for 30→60 FPS, 30→24 FPS, 2× speed, 0.5× speed, field-rate deinterlacing, and three-part split/merge recovery. AI mux tests use certified frame fixtures and do not invoke neural inference. GPU/model quality and the existing manual speech-isolation test remain outside automated verification.
