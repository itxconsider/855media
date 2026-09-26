using System;
using System.IO;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class FileUtilsTests
{
    [Fact]
    public async Task ReplaceFileWithRetryAsync_AtomicallyOverwritesExistingDestination()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var destPath = Path.Combine(tempDir, "video.mp4");
            var sourcePath = Path.Combine(tempDir, "video.mp4.transcoded.mp4");

            await File.WriteAllTextAsync(destPath, "original content");
            await File.WriteAllTextAsync(sourcePath, "new transcoded content");

            await FileUtils.ReplaceFileWithRetryAsync(sourcePath, destPath);

            Assert.True(File.Exists(destPath));
            Assert.False(File.Exists(sourcePath));
            var content = await File.ReadAllTextAsync(destPath);
            Assert.Equal("new transcoded content", content);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    [Fact]
    public async Task TryDeleteWithRetryAsync_CleansUpFileSafely()
    {
        var tempFile = Path.GetTempFileName();
        Assert.True(File.Exists(tempFile));

        await FileUtils.TryDeleteWithRetryAsync(tempFile);
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public void FileNameTemplate_TitleWithQuestionMark_EscapesCorrectly()
    {
        var video = new _855Media.Core.Resolving.VideoInfo(
            _855Media.Core.Resolving.VideoSource.YouTube,
            "dQw4w9WgXcQ",
            "https://youtube.com/watch?v=dQw4w9WgXcQ",
            "Why is this happening? Can we fix it? Yes!",
            "Test Author",
            "https://youtube.com/channel/test",
            100,
            TimeSpan.FromMinutes(1),
            []
        );

        var result = _855Media.Core.Downloading.FileNameTemplate.Apply(
            "$title",
            video,
            YoutubeExplode.Videos.Streams.Container.Mp4
        );

        Assert.DoesNotContain("?", result);
        Assert.Equal("Why is this happening Can we fix it Yes!.mp4", result);

        var video2 = video with { Title = "Who is this??? [Special Edition] / Part 1 * 100% real" };
        var result2 = _855Media.Core.Downloading.FileNameTemplate.Apply(
            "$title",
            video2,
            YoutubeExplode.Videos.Streams.Container.Mp4
        );
        Assert.DoesNotContain("?", result2);
        Assert.DoesNotContain("/", result2);
        Assert.DoesNotContain("*", result2);
        var invalidChars = Path.GetInvalidFileNameChars();
        Assert.DoesNotContain(result2, c => invalidChars.Contains(c));
    }

    [Theory]
    [InlineData("Can Silly Putty Stop a 50 Cal?", "Can Silly Putty Stop a 50 Cal")]
    [InlineData("What??? Why???", "What Why")]
    [InlineData("Episode 1: The New Era", "Episode 1 - The New Era")]
    [InlineData(
        "Bad/Path\\And:Illegal*Chars?Here<And>There|End.",
        "Bad - Path - And - IllegalCharsHereAndThereEnd"
    )]
    [InlineData("   . . .   ", "video")]
    [InlineData(null, "video")]
    [InlineData("", "video")]
    public void SanitizeFileName_CleansAllInvalidCharacters(string? input, string expected)
    {
        var sanitized = FileUtils.SanitizeFileName(input, fallback: "video");
        Assert.Equal(expected, sanitized);

        var invalidChars = Path.GetInvalidFileNameChars();
        Assert.DoesNotContain(sanitized, c => invalidChars.Contains(c));
        Assert.False(sanitized.EndsWith('.'));
        Assert.False(sanitized.EndsWith(' '));
    }

    [Theory]
    [InlineData(
        @"C:\Videos\Can Silly Putty Stop a 50 Cal?.mp4",
        @"C:\Videos\Can Silly Putty Stop a 50 Cal.mp4"
    )]
    [InlineData(@"D:\Downloads\Why? What?.webm", @"D:\Downloads\Why What.webm")]
    [InlineData(@"file:///C:/Users/test/video?name.mp4", @"C:\Users\test\videoname.mp4")]
    public void SanitizeFilePath_ProducesValidOsPaths(string input, string expected)
    {
        var sanitized = FileUtils.SanitizeFilePath(input);
        Assert.Equal(expected, sanitized);
        Assert.DoesNotContain("?", Path.GetFileName(sanitized));
    }
}
