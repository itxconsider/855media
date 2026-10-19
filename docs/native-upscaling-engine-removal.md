# Native-only upscaling engine change

Removed Real-ESRGAN executable discovery/configuration and the complete external frame extraction/inference/mux path, including its dependent face-restoration hooks, image-sequence checkpoints and progress handler. The UI now offers NVIDIA RTX and Fast Native only. Native filters, facial clarity, audio processing, split/merge, export timing and file checkpoints remain. The animation color preset now uses native filters and no longer advertises a neural model.

Saved numeric modes 1 and 2 (and unknown numeric modes) normalize to Fast Native. Numeric identities 0 (NVIDIA) and 3 (native) are preserved. Defaults use native processing; existing first-run NVIDIA selection remains supported. Removed face-stage fields in old JSON are ignored rather than executed.

The app project no longer copies tools/realesrgan. Runtime app/Core source and build configuration have no engine references. A fresh self-contained Windows publish to artifacts/native-only-publish-check contains no engine executable or model files. This validation output is not a new beta.

Validation: desktop build and Release publish passed; 158 upscaling tests passed with one pre-existing manual audio test skipped. Native media integration tests verify audio/video duration and frame counts across speed, deinterlace and target-FPS changes, alongside legacy mode migration and split recovery.

## Physical-file cleanup limitation

Automatic approval review rejected both the validated bulk folder removal and the explicitly named D:/repos/855Media/tools/realesrgan recursive deletion as blocked by policy. No deletion workaround was used. Inert source-tool files and copies in old generated output/package folders remain on disk; existing 3.1.3 and earlier executables remain historical builds. The new source neither discovers nor runs that engine, and new clean publishes exclude it. Physical removal of those retained copies is still outstanding.

Counted as implementation improvement 3/10 toward 3.1.4; no beta repackaged.
