using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace _855Media.Core.Dubbing;

public enum DubbingIssueSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record DubbingQualityIssue(
    SubtitleSegment Segment,
    string Code,
    DubbingIssueSeverity Severity,
    string Message
)
{
    public string Location => $"Line {Segment.Index} · {Segment.StartTime:hh\\:mm\\:ss\\.fff}";
}

/// <summary>Non-destructive script and take checks. Density is a review heuristic, not lip-sync proof.</summary>
public static class DubbingQualityService
{
    public static IReadOnlyList<DubbingQualityIssue> Analyze(
        IEnumerable<SubtitleSegment> segments,
        IEnumerable<MovieCharacter>? characters = null,
        bool requireTranslation = true
    )
    {
        var issues = new List<DubbingQualityIssue>();
        var script = segments.ToList();
        var cast = (characters ?? []).ToList();
        var duplicateIndexes = script
            .GroupBy(s => s.Index)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        foreach (var cue in script)
        {
            void Add(string code, DubbingIssueSeverity severity, string message) =>
                issues.Add(new(cue, code, severity, message));

            if (duplicateIndexes.Contains(cue.Index))
                Add(
                    "duplicate-index",
                    DubbingIssueSeverity.Error,
                    "Duplicate line number can overwrite generated audio. Renumber the script before rendering."
                );
            var seconds = (cue.EndTime - cue.StartTime).TotalSeconds;
            if (cue.StartTime < TimeSpan.Zero || seconds <= 0)
                Add(
                    "invalid-time",
                    DubbingIssueSeverity.Error,
                    "Start must be nonnegative and end must be after start."
                );
            if (
                string.IsNullOrWhiteSpace(cue.OriginalText)
                && string.IsNullOrWhiteSpace(cue.KhmerText)
            )
                Add(
                    "empty-dialogue",
                    DubbingIssueSeverity.Error,
                    "This cue has no dialogue. Remove it or enter the script."
                );
            else if (string.IsNullOrWhiteSpace(cue.KhmerText))
                Add(
                    "missing-translation",
                    requireTranslation ? DubbingIssueSeverity.Error : DubbingIssueSeverity.Warning,
                    "Translation is missing. Translate or enter an adapted line before generating the final dub."
                );
            else
            {
                if (
                    string.Equals(
                        cue.OriginalText.Trim(),
                        cue.KhmerText.Trim(),
                        StringComparison.Ordinal
                    ) && !cue.KhmerText.Any(c => c >= '\u1780' && c <= '\u17ff')
                )
                    Add(
                        "unchanged-translation",
                        DubbingIssueSeverity.Warning,
                        "Translation matches the source. Check whether this is intentional, such as a name or foreign-language line."
                    );
                if (
                    seconds > 0
                    && new StringInfo(cue.KhmerText).LengthInTextElements / seconds > 20
                )
                    Add(
                        "dense-dialogue",
                        DubbingIssueSeverity.Warning,
                        "Dialogue may be too dense for this slot. Shorten the adaptation or adjust timing after listening; this is a text estimate."
                    );
            }

            var actor = cue.CharacterId.HasValue
                ? cast.FirstOrDefault(c => c.Id == cue.CharacterId)
                : cast.FirstOrDefault(c =>
                    string.Equals(c.Name, cue.SpeakerName, StringComparison.OrdinalIgnoreCase)
                );
            if (actor == null)
                Add(
                    "unassigned-cast",
                    DubbingIssueSeverity.Warning,
                    "No matching cast member. Automatic voice assignment may change the intended speaker."
                );
            else if (
                actor.EnableRvc
                && (
                    string.IsNullOrWhiteSpace(actor.RvcModelPath)
                    || !File.Exists(actor.RvcModelPath)
                )
            )
                Add(
                    "missing-voice-model",
                    DubbingIssueSeverity.Error,
                    $"Voice model for {actor.Name} is missing. Relink it or disable voice conversion for this actor."
                );

            if (!string.IsNullOrWhiteSpace(cue.AudioClipPath) && !File.Exists(cue.AudioClipPath))
                Add(
                    "missing-take",
                    DubbingIssueSeverity.Warning,
                    "Saved take is missing. Regenerate this line before audio review."
                );
            if (
                cue.AudioDurationSeconds > 0
                && seconds > 0
                && cue.AudioDurationSeconds > seconds * 1.35 + 0.15
            )
                Add(
                    "take-too-long",
                    DubbingIssueSeverity.Warning,
                    "Recorded take needs substantial compression to fit. Prefer a shorter adaptation or retake over excessive time stretching."
                );
        }

        // Keep the furthest-running cue, so nested overlaps are detected too.
        SubtitleSegment? active = null;
        foreach (
            var cue in script
                .Where(s => s.StartTime >= TimeSpan.Zero && s.EndTime > s.StartTime)
                .OrderBy(s => s.StartTime)
        )
        {
            if (active != null && cue.StartTime < active.EndTime - TimeSpan.FromMilliseconds(40))
            {
                var sameActor =
                    cue.CharacterId.HasValue && active.CharacterId.HasValue
                        ? cue.CharacterId == active.CharacterId
                        : !string.IsNullOrWhiteSpace(cue.SpeakerName)
                            && string.Equals(
                                cue.SpeakerName,
                                active.SpeakerName,
                                StringComparison.OrdinalIgnoreCase
                            );
                issues.Add(
                    new(
                        cue,
                        "overlap",
                        sameActor ? DubbingIssueSeverity.Warning : DubbingIssueSeverity.Info,
                        $"Overlaps line {active.Index}. "
                            + (
                                sameActor
                                    ? "Check for duplicate captions or one actor speaking over their own take."
                                    : "Possible simultaneous speakers; preserve it if intentional."
                            )
                    )
                );
            }
            if (active == null || cue.EndTime > active.EndTime)
                active = cue;
        }
        return issues;
    }

    public static void ValidateForSynthesis(
        IEnumerable<SubtitleSegment> segments,
        bool requireTranslation,
        IEnumerable<MovieCharacter>? characters = null
    )
    {
        var errors = Analyze(segments, characters, requireTranslation)
            .Where(i => i.Severity == DubbingIssueSeverity.Error)
            .ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Studio QC: "
                    + string.Join(
                        " ",
                        errors.Take(5).Select(i => $"Line {i.Segment.Index}: {i.Message}")
                    )
            );
    }
}
