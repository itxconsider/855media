using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class TrackingTrajectoryTests
{
    internal static ActionAnalysisResult Path()
    {
        var result = new ActionAnalysisResult { ModeUsed = SmartTrackingMode.ObjectFocus };
        result.Trajectory.Add(
            new()
            {
                Timestamp = TimeSpan.Zero,
                NormalizedX = 0.2,
                NormalizedY = 0.4,
            }
        );
        result.Trajectory.Add(
            new()
            {
                Timestamp = TimeSpan.FromSeconds(4),
                NormalizedX = 0.8,
                NormalizedY = 0.6,
            }
        );
        result.Trajectory.Add(
            new()
            {
                Timestamp = TimeSpan.FromSeconds(8),
                NormalizedX = 0.3,
                NormalizedY = 0.5,
                IsSceneCut = true,
            }
        );
        return result;
    }

    [Fact]
    public void FocusChoosesOneRegionInsteadOfEmptyGapBetweenSubjects()
    {
        const int width = 200,
            height = 100;
        var frame = new byte[width * height * 3];
        foreach (int left in new[] { 15, 155 })
            for (int y = 30; y < 65; y++)
            for (int x = left; x < left + 25; x++)
            {
                int p = (y * width + x) * 3;
                frame[p] = 255;
                frame[p + 1] = 10;
                frame[p + 2] = 230;
            }
        var center = SmartActionAnalyzer.AnalyzeFrame(
            frame,
            width,
            height,
            SmartTrackingMode.ObjectFocus
        );
        Assert.True(center.X < 0.3 || center.X > 0.7, $"Framed empty gap at {center.X}");
    }

    [Fact]
    public void SmoothingSuppressesJitterAndLimitsMotionWithoutOvershoot()
    {
        Assert.Equal(0.5, SmartActionAnalyzer.SmoothAxis(0.5, 0.505, 1));
        Assert.InRange(SmartActionAnalyzer.SmoothAxis(0.2, 0.95, 1), 0.2, 0.4);
        Assert.InRange(SmartActionAnalyzer.SmoothAxis(0.8, 0.05, 2), 0.7, 0.8);
    }

    [Fact]
    public void InterpolatesInSourceTimeWithoutCrossingCuts()
    {
        var path = Path();
        Assert.Equal(0.5, path.GetCentroidAt(TimeSpan.FromSeconds(2)).X, 6);
        Assert.Equal(0.8, path.GetCentroidAt(TimeSpan.FromSeconds(7)).X);
        Assert.Equal(0.3, path.GetCentroidAt(TimeSpan.FromSeconds(8)).X);
        Assert.Equal(0.3, path.GetCentroidAt(TimeSpan.FromSeconds(100)).X);
        Assert.Equal(0.2, path.GetCentroidAt(TimeSpan.FromSeconds(-1)).X);
    }

    [Fact]
    public void DynamicCropAndZoomUseBoundedSourceTimeExpressions()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(
            AspectRatioMode.Square11,
            EditTargetResolution.Hd1080p,
            SmartTrackingMode.ObjectFocus,
            trajectory: Path()
        )!;
        Assert.Contains("clip((t-0)/4,0,1)", filter);
        Assert.Contains("clip((t-0)/4,0,1)", filter);
        Assert.Contains("max(0,min(iw-out_w", filter);
        Assert.Contains("gte(t,8)", filter);
        Assert.Contains(
            "clip((t-0)/4,0,1)",
            AspectRatioFilterBuilder.BuildMicroZoomFilter(5, trajectory: Path())
        );
    }

    [Fact]
    public void AnalysisCopiesAreIndependent()
    {
        var path = Path();
        var copy = path.Copy();
        copy.Trajectory[0].NormalizedX = 0.9;
        copy.Trajectory.Clear();
        Assert.Equal(0.2, path.Trajectory[0].NormalizedX);
        Assert.Equal(3, path.Trajectory.Count);
    }

    [Fact]
    public async Task CancellationIsNotTurnedIntoNeutralCachedAnalysis()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SmartActionAnalyzer.AnalyzeVideoAsync(
                "missing.mp4",
                SmartTrackingMode.ObjectFocus,
                cancellationToken: cancellation.Token
            )
        );
    }
}
