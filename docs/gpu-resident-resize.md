# GPU-resident H.264 resizing

Implemented 2026-10-23 in `GpuScalePolicy`, `GpuScaleCapabilities` and `VideoEditorService`.

Eligible Fast Native jobs use NVDEC CUDA surfaces, `scale_cuda` bicubic resize, and H.264 NVENC without downloading frames to CPU memory between these stages. Output remains 8-bit 4:2:0 H.264. Encoder quality, bitrate budget, metadata and audio options are retained. Source aspect ratio and existing even-dimension target sizing are retained. Existing stream-copy exports still bypass rendering entirely.

Eligibility: Fast Native, NVIDIA NVENC selected or resolved by Auto, H.264, a resize target (1080p/4K/2x/4x), Original aspect ratio/frame rate, normal playback speed, no active video effects. Facial clarity (enabled by default), denoise, deinterlace, zoom, grading and the RTX mode's existing sharpening retain the original processing pipeline. No effects are silently disabled. Choose Fast Native / NVIDIA NVENC / H.264, Original aspect ratio and frame rate, and disable those effects to use this path. The job log says `[GPU Resize]` when considered, with a specific capability/fallback explanation.

The FFmpeg filter list is checked asynchronously once per executable path/size/modification time with a bounded probe, shared across concurrent jobs. Caller cancellation is preserved. Missing CUDA filters retain the original pipeline without a failed render attempt. Input/device failures retry the original filter/NVENC pipeline, then the pre-existing CPU fallback if needed. Cancellation does not retry. A failed partial output is overwritten by the retry; existing completion/source cleanup remains after successful processing.

## Validation

- 165 upscaling tests passed, one unrelated speech-isolation integration test skipped; report `artifacts/gpu-scaler-check/gpu-scale.trx`.
- Actual RTX 5070 Ti test: full service export through CUDA filters with H.264/YUV420p, 1920x1080, preserved frame count, duration and audio; no fallback.
- Actual H.264 4:4:4 fixture: CUDA decoder path fails, original pipeline retries successfully and preserves frame count.
- Actual original FFmpeg (no scale_cuda): capability fallback exports successfully; `artifacts/gpu-scaler-check/legacy-ffmpeg.trx`.
- Policy cases cover effects, grading, aspect changes, RTX mode, frame-rate/speed changes, non-NVIDIA encoders and all resize targets.
- Isolated desktop build: zero warnings/errors, `artifacts/instagram-browser-build` reused.

## Measured performance and deployment

Two alternating runs each of native CPU bicubic scaling and CUDA scaling on the same synthetic 720p input, 960 frames, 4K output, identical P4 H.264 encoding to a null muxer: native 8.38/8.30 seconds, CUDA 8.10/8.10 seconds (about 3% shorter). This is a small synthetic scaler comparison on a busy PC, not a full-job or multi-worker performance guarantee. No visual-equivalence or quality-score claim is made; CUDA and software bicubic implementations can differ. Larger gains require the previous CPU resize stage to be the bottleneck.

The existing FFmpeg 8.1 app executable does not contain scale_cuda. A compatible Gyan 9.0.2 essentials build was downloaded from https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip and verified against its published SHA-256: `60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba`. Tested executable SHA-256: `3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec`. License copied to the isolated build's `tools/ffmpeg-gpu/LICENSE.txt`. Provider/source information: https://www.gyan.dev/ffmpeg/builds/ . NVIDIA pipeline reference: https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/ffmpeg-with-nvidia-gpu/index.html .

The compatible executable is staged only in `artifacts/instagram-browser-build/ffmpeg.exe`, beside the updated app. Restart into that isolated build to use it. It is not vendored into source or automatically substituted into the user's running installation; the existing FFmpeg downloader remains unchanged. Future release assembly must supply a CUDA-filter-capable FFmpeg to enable this path; old builds continue to function through fallback. Current packaged version remains 3.1.7; no package was produced, no running process was stopped and no saved jobs/settings were changed.
