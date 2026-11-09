# Sampled Smart Object Focus tracking

Implemented 2026-10-23. Smart focus now drives a timed crop path instead of discarding the trajectory and using one global mean for an entire export.

## Behavior

- Bounded low-resolution windows are spread from the beginning through the end of a known-duration clip. Turbo/Balanced/Quality sample budgets are at most 20/30/60 seconds of source material, using up to eight sequential seek windows at 1/1/2 FPS. Short clips are sampled continuously within the budget. Known duration from the existing source/preview probe avoids redundant duration probing. If duration is unavailable, analysis falls back to a bounded opening window.
- Positions interpolate between neighboring observed samples. Scene cuts and unobserved window gaps hold the previous position until the next observation, then reset; the app does not invent a pan through missing observations. Input timestamps are used before playback-speed changes. Aspect-ratio crops and action-anchored micro-zoom receive the path.
- Motion smoothing is based on elapsed sample time, with a small dead zone and a 0.20 normalized-coordinate/second maximum movement. Cuts reset directly. Saliency picks the strongest connected region in a 20x12 score grid instead of a whole-frame weighted mean that could frame empty space between two objects.
- Preview evaluates the same sampled path at its selected source timestamp and uses that position in the crop graph. Static-center behavior is retained. Saved centroids no longer suppress analysis after mode/source changes.
- Fixed 1 FPS timestamps previously advancing by 0.5 seconds. Cache keys now include window start/budget/mode/speed/FFmpeg path/source stamp; only successful complete windows are cached. Returned results are deep copies, cache size is bounded, and canceled analysis propagates cancellation without caching partial work. Decoder processes are stopped and awaited during cancellation.
- Long expressions use a balanced sum of ramps/steps to avoid FFmpeg parser recursion limits. Large graphs use the supported `-/filter:v` file-argument syntax in the job scratch directory, shared with CPU fallback, to avoid Windows command-line limits. The render checkpoint version changes so prior static-crop stages are not reused as dynamic tracking exports.

## Validation

183 Editor tests passed with one unrelated speech-isolation test skipped; report `artifacts/editor-effects-check/smart-tracking.trx`. Real FFmpeg checks verify 1/2 FPS timing, short-budget samples reaching the end of a clip, independent cached snapshots, reanalysis despite saved coordinates, full 2x-speed export duration, pixel hashes matching expected crop positions at multiple timestamps, cut holds, a 120-point graph through a filter file and actual split-screen preview image generation. Unit checks cover two separated salient subjects, dead-zone/movement bounds, interpolation and cancellation. Existing GPU, queue, Part, grading and recovery tests passed. Actual responsive/Editor/grading diagnostics also passed during implementation. Full solution build passed with zero warnings/errors, reusing `artifacts/instagram-browser-build`.

## Limits and deployment

This remains saliency/skin-color/motion-based analysis, not trained object recognition, identity tracking or a true face detector. Multiple equally salient subjects, camera motion, low contrast and occlusion can still choose the wrong region. Sparse windows can miss brief movement or cuts in gaps and can reset abruptly at boundaries. Real user-footage/DPI validation is pending; no accuracy or FPS improvement is claimed for arbitrary videos. This improvement addresses the actual tracking connection and bounded detection stability; a trained face/subject detector is a separate future change.

Source/build only: existing running jobs, settings and packaged 3.1.9 archive were not replaced. One improvement credit brings the next-package counter to 5/10.
