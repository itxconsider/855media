# Editor module naming

Completed 2026-10-23. The module's source vocabulary now matches its Editor UI:

| Previous | Current |
| --- | --- |
| VideoUpscalerView / VideoUpscalerViewModel | VideoEditorView / VideoEditorViewModel |
| VideoUpscaleService | VideoEditorService |
| Core.Upscaling and Core.Tests.Upscaling folders/namespaces | Core.Editing and Core.Tests.Editing |
| UpscaleJob / UpscaleJobStatus | EditJob / EditJobStatus |
| UpscaleCheckpoint / SplitAndUpscalePipeline | EditCheckpoint / SplitAndEditPipeline |
| UpscaleTargetResolution / UpscaleVideoCodec / UpscaleModelType | EditTargetResolution / EditVideoCodec / EditModelType |
| DashboardTab.Upscaler, SelectUpscalerTab, VideoUpscaler | DashboardTab.Editor, SelectEditorTab, VideoEditor |
| SettingsService.Upscaler* C# properties | SettingsService.Editor* C# properties |
| CheckUpscaler / Upscaler hardware readiness | CheckEditor / Editor hardware readiness |

Updated file names, bindings, view factories, DI, localization keys, internal functions/fields, tests, diagnostics and current technical documentation references. Historical release records retain their original descriptions. The download format's Upscaled badge and YoutubeExplode's IsVideoUpscaled are unrelated to the Editor module and retain their meaning.

## Persistence and licensing

C# identifiers change; serialized contracts remain compatible. Explicit JsonPropertyName attributes retain existing Upscaler settings keys and EnableSplitAndUpscale/MergeAfterUpscale/DeleteSourceAfterUpscale job keys. Existing enum values retain their numeric ordinals. DashboardTab.Editor accepts the legacy Upscaler string. LicenseFeature.Editing retains the legacy signed Upscaling feature key; signed payloads are never rewritten and expiry checks are unchanged. Legacy queue filename, scratch root and part/checkpoint filenames remain intentionally stable so recovery finds existing work. No migration or clearing of saved user data is required.

## Validation

- Full solution including app/Core/tests/KeyGen built with zero warnings/errors.
- 422 focused Editing/Licensing/hardware/downloading tests passed, one unrelated speech-isolation test skipped. Includes actual CUDA rendering and original-filter recovery fixtures, old job JSON round-trip and signed legacy feature/expiry cases. Report: artifacts/editor-rename/editor-rename.trx.
- Actual source-generated SettingsService serializer loaded and round-tripped legacy output directory, concurrency, split flags and grading values; dashboard string tab compatibility passed.
- Actual renamed Editor view rendered through existing responsive diagnostics; numeric grading, expansion, zoom/reset, modal and browser checks passed.
- Source-reference scan retains old module spelling only for compatibility contracts, fixtures and the negative test that verifies the old view type no longer exists. Legitimate source-video upscaling metadata remains unchanged.

Isolated output reused: artifacts/instagram-browser-build/855Media.exe. Running app and packaged 3.1.7 ZIP were not replaced. One improvement credit brings the source-change count to 21/10; no package was produced.
