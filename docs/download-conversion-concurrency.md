# Download conversion concurrency

Settings → Downloads → **Simultaneous conversions** controls the shared H.264
compatibility conversion queue. The range is 1–10, and the default is **2**, including
existing settings files that do not contain the new `ConversionParallelLimit` property.

Changes apply immediately and are persisted through the normal settings save flow.
Increasing the limit admits queued conversions. Decreasing it lets running conversions
finish and delays new work until the active count is below the new limit. Canceling a
queued conversion removes its claim on capacity. Each slot is released once, including
when conversion or cleanup fails.

Download tasks retain their download slot through final processing. Therefore, the
effective conversion concurrency is also bounded by **Parallel limit**. Set both values
to at least 2 to allow two downloading videos to convert simultaneously. The download
default is unchanged. Higher conversion counts use more CPU/GPU, memory and disk I/O.

H.264/AAC validation, stream-copy optimizations, per-process CPU thread limits and
hardware encoder fallbacks remain in place. Compatible files do not require encoding.

Validation:

- 149 focused downloading tests passed, including queue limits, live resizing,
  cancellation, invalid values, idempotent slot release and disposal.
- Desktop build passed with zero warnings/errors in an isolated output directory;
  the normal Debug output was locked by the running app and Visual Studio.
- Isolated settings save/load, default value, invalid-value normalization and live
  slider binding checks passed.
- Settings → Downloads rendered at 860 × 620 and was visually checked.

No running app was replaced and no release package was produced. Release progress is
8/10 toward 3.1.6.
