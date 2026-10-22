using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class EditRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "855Media_RecoveryTests",
        Guid.NewGuid().ToString("N")
    );

    public EditRecoveryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void SplitClipRequiresSuccessfulCompletionAndMatchingSourceSettings()
    {
        var path = Path.Combine(_directory, "part.mp4");
        File.WriteAllBytes(path, new byte[4096]);
        Assert.False(EditCheckpoint.IsFileComplete(path, "settings"));
        EditCheckpoint.CompleteFile(path, "settings");
        Assert.True(EditCheckpoint.IsFileComplete(path, "settings"));
        Assert.False(EditCheckpoint.IsFileComplete(path, "new settings"));
        File.AppendAllText(path, "partial replacement");
        Assert.False(EditCheckpoint.IsFileComplete(path, "settings"));
    }

    [Fact]
    public void SourceAndRenderSettingsInvalidateCheckpoints()
    {
        var path = Path.Combine(_directory, "source.mp4");
        File.WriteAllText(path, "source");
        var job = new EditJob { FilePath = path };
        var original = EditCheckpoint.RenderKey(job);
        job.TargetFramerate = TargetFramerate.Fps60;
        Assert.NotEqual(original, EditCheckpoint.RenderKey(job));
        var updated = EditCheckpoint.RenderKey(job);
        File.AppendAllText(path, "changed source");
        Assert.NotEqual(updated, EditCheckpoint.RenderKey(job));
    }

    [Theory]
    [InlineData(40, 1200)]
    [InlineData(60, 1800)]
    [InlineData(10, 300)]
    public void SplitChildrenKeepSettingsAndUseTheirOwnDuration(double duration, long frames)
    {
        var master = new EditJob
        {
            VideoFps = 30,
            TotalFrames = 3900,
            TargetFramerate = TargetFramerate.Fps60,
            PlaybackSpeed = 1.25,
        };
        var child = SplitAndEditPipeline.CreateChildJob(master, "part.mp4", "output.mp4", duration);
        Assert.Equal(TargetFramerate.Fps60, child.TargetFramerate);
        Assert.Equal(1.25, child.PlaybackSpeed);
        Assert.Equal(frames, child.TotalFrames);
        Assert.False(child.EnableSplitAndEdit);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        File.Delete(_directory + ".complete.json");
    }
}
