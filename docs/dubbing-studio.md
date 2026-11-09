# Dubbing studio: dialogue adaptation and delivery review

The Dubbing workspace now has a Studio QC tab alongside Script, Timeline,
Cast & Voices and Render Queue. The existing Khmer output workflow is retained.

## Movie workflow

1. Import the movie and subtitles, or transcribe its dialogue. Review transcription
   against the original audio before translation; music and crowd scenes need listening.
2. Assign named cast members and relink missing voice models. Record names,
   pronunciations, relationships, pronouns, genre and recurring terms in the translation
   brief in Studio QC. Save the project to retain this brief.
3. Translate missing lines. Each Gemini batch receives the same brief, source language,
   speaker, gender, emotion and the actual cue duration. The prompt asks for concise
   spoken adaptation without inventing dialogue or relationships. Already edited
   translations remain untouched. Web fallback does not use the brief.
4. Run studio QC. Use Go to line to select the affected script cue and seek the
   timeline. Duplicate numbers, invalid intervals, empty dialogue, missing translations
   and missing cast voice models are errors. Overlaps, missing saved takes, dense
   adaptation and long audio takes invite review. Different actors overlapping is a
   note, not a prohibition: simultaneous dialogue is often intentional.
5. Generate and audition voices. Rewrite rushed lines before relying on heavy time
   compression. Repeat QC after edits, cast changes and retakes; reports are snapshots.
6. Export. Structural script errors are checked before translation in the pipeline;
   translation completeness is checked again before synthesis, including queued jobs.
   Missing selected global or referenced cast conversion models stop the render.
   Existing mixing, ducking, stem separation and normalization controls remain available.

Require translated dialogue defaults on in new and older saved desktop projects.
Turning it off explicitly permits the existing source-text fallback; it does not turn
off structural or missing-model guards. Programmatic DubbingJob callers retain their
existing default and can set RequireTranslatedDialogue explicitly. The project and
queued desktop jobs preserve the policy and translation brief.

## Validation (2026-10-26)

- 232 Dubbing tests passed, including 14 new cases across invalid timing, duplicate
  take IDs, translation policy, intentional/nested overlaps, missing assets, density,
  take duration, project compatibility and the actual serialized Gemini request.
- Actual Avalonia Dubbing view rendered at 1280x820. Brief two-way editing and all
  three issue-navigation bindings passed with synthetic data. The issue list and
  brief remain accessible in the existing 320-pixel workspace panel.
- Main desktop Debug build passed without warnings or errors.
- Reproduce UI check: `dotnet run --project tools/diagnostics/DubbingStudioCheck`.
  Evidence: `artifacts/dubbing-studio/studio-qc-1280x820.png` and `focused.trx`.

No live translation/TTS service was called and no real movie was exported in this
validation. Text-density warnings estimate pacing; they do not prove lip sync or
translation correctness. This update does not add visual lip animation, certified
speaker diarization or a new language synthesis engine. Real film listening and
translation review remain necessary, especially with overlapping actors, songs,
whispers and noisy scenes. Large runtime/model assets still require local provisioning.
No package was produced; existing release ZIPs are unchanged.
