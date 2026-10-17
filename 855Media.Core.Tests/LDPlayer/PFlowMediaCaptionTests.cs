using System;
using System.IO;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowMediaCaptionTests
{
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
            Assert.Equal("Pool repair", PFlowMediaCaption.Resolve(media, false));
            File.WriteAllText(text, "  ");
            Assert.Equal("Pool repair", PFlowMediaCaption.Resolve(media, true));
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
        older.ReelTitleFromTextFile = false;
        older.PhotoTitleFromTextFile = true;
        older.VideoTitleFromTextFile = false;
        var saved = JsonSerializer.Deserialize<FacebookPageTarget>(
            JsonSerializer.Serialize(older)
        )!;
        Assert.False(saved.ReelTitleFromTextFile);
        Assert.True(saved.PhotoTitleFromTextFile);
        Assert.False(saved.VideoTitleFromTextFile);
    }
}
