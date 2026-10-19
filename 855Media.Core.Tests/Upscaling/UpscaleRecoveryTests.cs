using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class UpscaleRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "855Media_RecoveryTests",
        Guid.NewGuid().ToString("N")
    );

    public UpscaleRecoveryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void SplitClipRequiresSuccessfulCompletionAndMatchingSourceSettings()
    {
        var path = Path.Combine(_directory, "part.mp4");
        File.WriteAllBytes(path, new byte[4096]);
        Assert.False(UpscaleCheckpoint.IsFileComplete(path, "settings"));
        UpscaleCheckpoint.CompleteFile(path, "settings");
        Assert.True(UpscaleCheckpoint.IsFileComplete(path, "settings"));
        Assert.False(UpscaleCheckpoint.IsFileComplete(path, "new settings"));
        File.AppendAllText(path, "partial replacement");
        Assert.False(UpscaleCheckpoint.IsFileComplete(path, "settings"));
    }

    [Fact]
    public void SourceAndRenderSettingsInvalidateCheckpoints()
    {
        var path = Path.Combine(_directory, "source.mp4");
        File.WriteAllText(path, "source");
        var job = new UpscaleJob { FilePath = path };
        var original = UpscaleCheckpoint.RenderKey(job);
        job.TargetFramerate = TargetFramerate.Fps60;
        Assert.NotEqual(original, UpscaleCheckpoint.RenderKey(job));
        var updated = UpscaleCheckpoint.RenderKey(job);
        File.AppendAllText(path, "changed source");
        Assert.NotEqual(updated, UpscaleCheckpoint.RenderKey(job));
    }

    [Theory]
    [InlineData(40, 1200)]
    [InlineData(60, 1800)]
    [InlineData(10, 300)]
    public void SplitChildrenKeepSettingsAndUseTheirOwnDuration(double duration, long frames)
    {
        var master = new UpscaleJob
        {
            VideoFps = 30,
            TotalFrames = 3900,
            TargetFramerate = TargetFramerate.Fps60,
            PlaybackSpeed = 1.25,
        };
        var child = SplitAndUpscalePipeline.CreateChildJob(
            master,
            "part.mp4",
            "output.mp4",
            duration
        );
        Assert.Equal(TargetFramerate.Fps60, child.TargetFramerate);
        Assert.Equal(1.25, child.PlaybackSpeed);
        Assert.Equal(frames, child.TotalFrames);
        Assert.False(child.EnableSplitAndUpscale);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        File.Delete(_directory + ".complete.json");
    }
}
