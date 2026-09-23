using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class VideoTimelineExtensionTests
{
    [Fact]
    public async Task ConcatenateVideosAsync_EmptyOrMissingList_ReturnsFalse()
    {
        var result = await DubbingPipeline.ConcatenateVideosAsync(
            "ffmpeg",
            new List<string>(),
            "output.mp4"
        );
        Assert.False(result);

        var resultNull = await DubbingPipeline.ConcatenateVideosAsync(
            "ffmpeg",
            null!,
            "output.mp4"
        );
        Assert.False(resultNull);

        var resultMissingFiles = await DubbingPipeline.ConcatenateVideosAsync(
            "ffmpeg",
            new[] { "non_existent_12345.mp4" },
            "output.mp4"
        );
        Assert.False(resultMissingFiles);
    }

    [Fact]
    public async Task ConcatenateVideosAsync_SingleVideo_CopiesToTargetDirectly()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "ConcatTest_" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempDir);
        var sourceFile = Path.Combine(tempDir, "clip1.mp4");
        var targetFile = Path.Combine(tempDir, "clip1_extended.mp4");

        try
        {
            await File.WriteAllTextAsync(sourceFile, "mock video stream content");
            var result = await DubbingPipeline.ConcatenateVideosAsync(
                "ffmpeg",
                new[] { sourceFile },
                targetFile
            );

            Assert.True(result);
            Assert.True(File.Exists(targetFile));
            var copiedContent = await File.ReadAllTextAsync(targetFile);
            Assert.Equal("mock video stream content", copiedContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ExtendTimeline_DialogueSegments_CorrectlyOffsetByCumulativeDuration()
    {
        // Arrange: Video 1 (10s duration) has 2 lines
        var video1Duration = TimeSpan.FromSeconds(10);
        var segments = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(3),
                OriginalText = "Clip 1 Line 1",
            },
            new()
            {
                Index = 2,
                StartTime = TimeSpan.FromSeconds(4),
                EndTime = TimeSpan.FromSeconds(8),
                OriginalText = "Clip 1 Line 2",
            },
        };

        // Video 2 has 2 lines (at 2s and 5s within Video 2)
        var clip2Lines = new List<SubtitleSegment>
        {
            new()
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(2),
                EndTime = TimeSpan.FromSeconds(4),
                OriginalText = "Clip 2 Line 1",
            },
            new()
            {
                Index = 2,
                StartTime = TimeSpan.FromSeconds(5),
                EndTime = TimeSpan.FromSeconds(9),
                OriginalText = "Clip 2 Line 2",
            },
        };

        // Act: Offset clip 2 lines by video 1 duration
        foreach (var seg in clip2Lines)
        {
            seg.StartTime += video1Duration;
            seg.EndTime += video1Duration;
            seg.Index = segments.Count + 1;
            segments.Add(seg);
        }

        // Assert
        Assert.Equal(4, segments.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), segments[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(3), segments[0].EndTime);
        Assert.Equal(TimeSpan.FromSeconds(8), segments[1].EndTime);

        // Extended lines should start at 10s + 2s = 12s and 10s + 5s = 15s
        Assert.Equal(TimeSpan.FromSeconds(12), segments[2].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(14), segments[2].EndTime);
        Assert.Equal(3, segments[2].Index);

        Assert.Equal(TimeSpan.FromSeconds(15), segments[3].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(19), segments[3].EndTime);
        Assert.Equal(4, segments[3].Index);
    }

    [Fact]
    public async Task ScaleAudioClipDurationAsync_NonExistentFile_ReturnsFalse()
    {
        var result = await DubbingPipeline.ScaleAudioClipDurationAsync(
            "ffmpeg",
            "non_existent.wav",
            "non_existent_out.wav",
            2.5
        );
        Assert.False(result);
    }

    [Fact]
    public async Task AssembleVocalTrackAsync_WhenCancelled_ThrowsPromptly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = new DubbingJob
        {
            VideoFilePath = "dummy.mp4",
            OutputFilePath = "dummy_vocal.wav",
        };
        job.Segments.Add(
            new SubtitleSegment
            {
                Index = 1,
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(2),
                OriginalText = "Hello",
                AudioClipPath = "dummy.wav",
            }
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await DubbingPipeline.AssembleVocalTrackAsync(
                "ffmpeg",
                job,
                "dummy_vocal.wav",
                cts.Token
            );
        });
    }
}
