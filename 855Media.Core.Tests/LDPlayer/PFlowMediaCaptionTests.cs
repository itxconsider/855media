using System;
using System.IO;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowMediaCaptionTests
{
    [Theory]
    [InlineData(null, "Pool repair")]
    [InlineData("", "Pool repair")]
    [InlineData("  \r\n ", "Pool repair")]
    [InlineData(" #plumbing ", "Pool repair\n#plumbing")]
    [InlineData("Follow for more\n#plumbing", "Pool repair\nFollow for more\n#plumbing")]
    public void SuffixPreservesOriginalAndSupportsOptionalMultilineText(
        string? suffix,
        string expected
    )
    {
        Assert.Equal(expected, PFlowMediaCaption.Resolve("Pool repair.mp4", false, suffix));
    }

    [Fact]
    public void TextOptionUsesSidecarAndFilenameFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var media = Path.Combine(directory, "Pool repair.mp4");
            var text = Path.ChangeExtension(media, ".txt");
            Assert.Equal("Pool repair", PFlowMediaCaption.Resolve(media, true));
            File.WriteAllText(text, "  Caption from text\nsecond line  ");
            Assert.Equal("Caption from text\nsecond line", PFlowMediaCaption.Resolve(media, true));
            Assert.Equal(
                "Caption from text\nsecond line\n#repair",
                PFlowMediaCaption.Resolve(media, true, "#repair")
            );
            Assert.Equal(
                "Pool repair\n#repair",
                PFlowMediaCaption.Resolve(media, false, "#repair")
            );
            Assert.Equal("Pool repair", PFlowMediaCaption.Resolve(media, false));
            File.WriteAllText(text, "  ");
            Assert.Equal("Pool repair", PFlowMediaCaption.Resolve(media, true));
            Assert.Equal("Pool repair\n#repair", PFlowMediaCaption.Resolve(media, true, "#repair"));
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Pool repair.txt"));
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void PerTypeOptionsRoundTripIndependentlyAndOlderFilesRemainUnset()
    {
        var older = JsonSerializer.Deserialize<FacebookPageTarget>(
            "{\"UseTitleFromTextFile\":true}"
        )!;
        Assert.Null(older.ReelTitleFromTextFile);
        Assert.True(older.UseTitleFromTextFile);
        Assert.Equal("", older.ReelCaptionSuffix);
        Assert.Equal("", older.PhotoCaptionSuffix);
        older.ReelCaptionSuffix = "Follow for more\n#reels";
        older.PhotoCaptionSuffix = "#photos";
        older.ReelTitleFromTextFile = false;
        older.PhotoTitleFromTextFile = true;
        older.VideoTitleFromTextFile = false;
        var saved = JsonSerializer.Deserialize<FacebookPageTarget>(
            JsonSerializer.Serialize(older)
        )!;
        Assert.False(saved.ReelTitleFromTextFile);
        Assert.True(saved.PhotoTitleFromTextFile);
        Assert.False(saved.VideoTitleFromTextFile);
        Assert.Equal("Follow for more\n#reels", saved.ReelCaptionSuffix);
        Assert.Equal("#photos", saved.PhotoCaptionSuffix);
    }
}
