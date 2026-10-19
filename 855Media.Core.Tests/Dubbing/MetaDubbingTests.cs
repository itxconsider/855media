using System.Text.Json;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class MetaDubbingTests
{
    [Fact]
    public async Task CancelledWorkerRequest_DoesNotLaunchTheInterpreter()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var options = new MetaDubbingOptions { PythonPath = "interpreter-that-must-not-run" };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MetaDubbingService().CheckAsync(options, cancellation.Token)
        );
    }

    [Theory]
    [InlineData(@"D:\movie folder\音声.wav", true, "/mnt/d/movie folder/音声.wav")]
    [InlineData("C:/work/clip.wav", true, "/mnt/c/work/clip.wav")]
    [InlineData("/home/user/python", true, "/home/user/python")]
    [InlineData(@"D:\clip.wav", false, @"D:\clip.wav")]
    public void WorkerPaths_HandleSpacesUnicodeAndNativePaths(
        string input,
        bool useWsl,
        string expected
    ) => Assert.Equal(expected, MetaDubbingService.ToWorkerPath(input, useWsl));

    [Fact]
    public void WorkerPaths_RejectUnmappedNetworkPaths() =>
        Assert.Throws<NotSupportedException>(() =>
            MetaDubbingService.ToWorkerPath(@"\\server\share\clip.wav", true)
        );

    [Fact]
    public void ParseSegments_PreservesSpeechTimingAndRejectsInvalidIntervals()
    {
        using var response = JsonDocument.Parse(
            """
            {"segments":[
                {"start":25.2,"end":28.1,"text":"second line"},
                {"start":2.5,"end":4.1,"text":"first line"},
                {"start":-1,"end":2,"text":"invalid"},
                {"start":4,"end":3,"text":"invalid"},
                {"start":5,"end":6,"text":"  "}
            ]}
            """
        );
        var segments = MetaDubbingService.ParseSegments(response.RootElement);
        Assert.Equal(2, segments.Count);
        Assert.Equal(2.5, segments[0].StartSeconds);
        Assert.Equal(28.1, segments[1].EndSeconds);
        Assert.All(segments, segment => Assert.Empty(segment.KhmerText));
        Assert.Equal(new[] { 1, 2 }, segments.Select(segment => segment.Index));
    }

    [Fact]
    public async Task Project_RoundTripsProviderChoicesAndLoadsOldProjectsWithProvidersDisabled()
    {
        var file = Path.Combine(Path.GetTempPath(), $"meta_project_{Guid.NewGuid():N}.855dub");
        try
        {
            var project = new DubbingProject
            {
                Meta = new MetaDubbingOptions
                {
                    EnableSamAudio = true,
                    EnableOmnilingualAsr = true,
                    PythonPath = "/home/user/855media-meta/bin/python",
                },
            };
            await project.SaveAsync(file);
            var loaded = await DubbingProject.LoadAsync(file);
            Assert.True(loaded.Meta.EnableSamAudio);
            Assert.True(loaded.Meta.EnableOmnilingualAsr);
            Assert.Equal(project.Meta.PythonPath, loaded.Meta.PythonPath);
            var copy = loaded.Meta.Copy();
            copy.EnableSamAudio = false;
            Assert.True(loaded.Meta.EnableSamAudio);

            await File.WriteAllTextAsync(file, "{}");
            loaded = await DubbingProject.LoadAsync(file);
            Assert.False(loaded.Meta.EnableSamAudio);
            Assert.False(loaded.Meta.EnableOmnilingualAsr);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
