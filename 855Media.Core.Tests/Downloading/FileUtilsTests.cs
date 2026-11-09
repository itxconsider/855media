using System;
using System.IO;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class FileUtilsTests
{
    [Fact]
    public void DownloadPathBudgetReservesNativeToolSuffixesAndIsIdempotent()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = @"D:\repos\855Media\artifacts\instagram-import-live-20261109\downloads";
        var path = FileUtils.SanitizeFilePath(
            Path.Combine(root, "[0001] " + new string('x', 220) + ".mp4")
        );
        Assert.InRange(path.Length, 1, FileUtils.MaximumDownloadPathLength);
        Assert.True((path + ".transcoded.mp4").Length < 260);
        Assert.True((path + ".f137.mp4.part").Length < 260);
        Assert.EndsWith(".mp4", path);
        Assert.StartsWith("[0001]", Path.GetFileName(path));
        Assert.Equal(path, FileUtils.SanitizeFilePath(path));
    }

    [Fact]
    public void TruncatedLongCaptionsRemainDistinctAndUnicodeSafe()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = @"D:\repos\855Media\artifacts\instagram-import-live-20261109\downloads";
        var first = FileUtils.SanitizeFilePath(
            Path.Combine(root, new string('x', 210) + "first.mp4")
        );
        var second = FileUtils.SanitizeFilePath(
            Path.Combine(root, new string('x', 210) + "second.mp4")
        );
        Assert.NotEqual(first, second);
        var emojis = FileUtils.SanitizeFilePath(
            Path.Combine(root, string.Concat(Enumerable.Repeat("💪", 120)) + ".mp4")
        );
        Assert.DoesNotContain(
            "\uFFFD",
            System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(emojis))
        );
        Assert.False(
            char.IsHighSurrogate(FileUtils.SanitizeFileName(new string('x', 199) + "💪")[^1])
        );
    }

    [Fact]
    public void DeepDestinationFailsClearlyButShortFileThatFitsIsAllowed()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var deep = @"D:\" + new string('d', 220);
        Assert.Throws<PathTooLongException>(() =>
            FileUtils.SanitizeFilePath(Path.Combine(deep, "too-long-name.mp4"))
        );
        var shortPath = FileUtils.SanitizeFilePath(
            Path.Combine(@"D:\" + new string('d', 215), "v.mp4")
        );
        Assert.EndsWith("v.mp4", shortPath);
    }

    [Fact]
    public void CollisionReservationsKeepSafeStablePathsAndExistingBytes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "855-path-budget-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var requested = Path.Combine(root, new string('x', 200) + ".mp4");
            var reserved = FileUtils.GetAvailableDownloadFilePath(requested);
            File.WriteAllText(reserved, "keep-existing");
            var next = FileUtils.GetAvailableDownloadFilePath(requested);
            Assert.NotEqual(reserved, next);
            Assert.Equal(next, FileUtils.SanitizeFilePath(next));
            File.WriteAllText(next, "keep-second");
            var third = FileUtils.GetAvailableDownloadFilePath(requested);
            Assert.NotEqual(next, third);
            Assert.Equal("keep-existing", File.ReadAllText(reserved));
            Assert.Equal("keep-second", File.ReadAllText(next));
            if (OperatingSystem.IsWindows())
                Assert.True((third + ".transcoded.mp4").Length < 260);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

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
