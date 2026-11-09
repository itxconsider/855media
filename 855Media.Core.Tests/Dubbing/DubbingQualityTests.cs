using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class DubbingQualityTests
{
    private static SubtitleSegment Cue(int index = 1, double start = 0, double end = 3) =>
        new()
        {
            Index = index,
            StartTime = TimeSpan.FromSeconds(start),
            EndTime = TimeSpan.FromSeconds(end),
            OriginalText = "Hello",
            KhmerText = "សួស្តី",
            SpeakerName = "Actor",
        };

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 2)]
    public void InvalidTimingIsNotHiddenByClampedDuration(double start, double end) =>
        Assert.Contains(
            DubbingQualityService.Analyze([Cue(start: start, end: end)]),
            i => i.Code == "invalid-time" && i.Severity == DubbingIssueSeverity.Error
        );

    [Fact]
    public void DuplicateIndexesCannotOverwriteTakeFiles() =>
        Assert.Throws<InvalidOperationException>(() =>
            DubbingQualityService.ValidateForSynthesis([Cue(), Cue(start: 4, end: 6)], true)
        );

    [Fact]
    public void MissingTranslationBlocksStrictSynthesisButAllowsExplicitSourceFallback()
    {
        var cue = Cue();
        cue.KhmerText = "";
        Assert.Throws<InvalidOperationException>(() =>
            DubbingQualityService.ValidateForSynthesis([cue], true)
        );
        DubbingQualityService.ValidateForSynthesis([cue], false);
    }

    [Fact]
    public void EmptyDialogueIsAlwaysAnError()
    {
        var cue = Cue();
        cue.KhmerText = "";
        cue.OriginalText = " ";
        Assert.Throws<InvalidOperationException>(() =>
            DubbingQualityService.ValidateForSynthesis([cue], false)
        );
    }

    [Fact]
    public void NestedOverlapsAreDetectedWithoutRewritingMovieTiming()
    {
        var cues = new[] { Cue(1, 0, 10), Cue(2, 1, 2), Cue(3, 4, 5) };
        var issues = DubbingQualityService.Analyze(cues);
        Assert.Equal(2, issues.Count(i => i.Code == "overlap"));
        Assert.Equal(10, cues[0].EndSeconds);
        Assert.Same(cues[2], issues.Last(i => i.Code == "overlap").Segment);
    }

    [Fact]
    public void DifferentActorOverlapIsInformationalAndDoesNotBlockSynthesis()
    {
        var first = Cue();
        first.CharacterId = Guid.NewGuid();
        var second = Cue(2, 1, 4);
        second.CharacterId = Guid.NewGuid();
        Assert.Contains(
            DubbingQualityService.Analyze([first, second]),
            i => i.Code == "overlap" && i.Severity == DubbingIssueSeverity.Info
        );
        DubbingQualityService.ValidateForSynthesis([first, second], true);
    }

    [Fact]
    public void AdjacentCuesAndSmallRoundingOverlapAreNotFlagged() =>
        Assert.DoesNotContain(
            DubbingQualityService.Analyze([Cue(), Cue(2, 2.98, 5)]),
            i => i.Code == "overlap"
        );

    [Fact]
    public void MissingRvcModelAndMissingSavedTakeAreReported()
    {
        var actor = new MovieCharacter
        {
            Name = "Actor",
            EnableRvc = true,
            RvcModelPath = "missing-voice.pth",
        };
        var cue = Cue();
        cue.CharacterId = actor.Id;
        cue.AudioClipPath = "missing-take.wav";
        var issues = DubbingQualityService.Analyze([cue], [actor]);
        Assert.Contains(
            issues,
            i => i.Code == "missing-voice-model" && i.Severity == DubbingIssueSeverity.Error
        );
        Assert.Contains(issues, i => i.Code == "missing-take");
        Assert.Throws<InvalidOperationException>(() =>
            DubbingQualityService.ValidateForSynthesis([cue], true, [actor])
        );
    }

    [Fact]
    public void OverlongTakeAndDenseAdaptationRequestHumanReview()
    {
        var cue = Cue();
        cue.KhmerText = new string('ក', 100);
        cue.AudioDurationSeconds = 8;
        var issues = DubbingQualityService.Analyze([cue]);
        Assert.Contains(issues, i => i.Code == "take-too-long");
        Assert.Contains(issues, i => i.Code == "dense-dialogue");
    }

    [Fact]
    public void UnchangedKhmerSourceIsNotFalselyReportedAsUntranslated()
    {
        var cue = Cue();
        cue.OriginalText = cue.KhmerText;
        Assert.DoesNotContain(
            DubbingQualityService.Analyze([cue]),
            i => i.Code == "unchanged-translation"
        );
    }

    [Fact]
    public async Task TranslationPolicyRoundTripsAndOldProjectDefaultsToStrict()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".855dub");
        try
        {
            await new DubbingProject
            {
                RequireTranslatedDialogue = false,
                TranslationBrief = "Use respectful speech for the teacher.",
            }.SaveAsync(path);
            var restored = await DubbingProject.LoadAsync(path);
            Assert.False(restored.RequireTranslatedDialogue);
            Assert.Equal("Use respectful speech for the teacher.", restored.TranslationBrief);
            await File.WriteAllTextAsync(path, "{\"ProjectName\":\"Old movie\"}");
            Assert.True((await DubbingProject.LoadAsync(path)).RequireTranslatedDialogue);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
