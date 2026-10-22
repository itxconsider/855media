using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Resolving;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public sealed class MediaTextFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "855Media-caption-test-" + Guid.NewGuid().ToString("N")
    );
    private string MediaPath => Path.Combine(_directory, "photo.jpg");
    private string TextPath => Path.ChangeExtension(MediaPath, ".txt");

    public MediaTextFileTests() => Directory.CreateDirectory(_directory);

    private static VideoInfo Photo(string? caption) =>
        new(
            VideoSource.FacebookPhoto,
            "101",
            "https://example.test/photo.jpg",
            "May be an image of water heater",
            "Author",
            null,
            null,
            null,
            [],
            Caption: caption
        );

    [Theory]
    [InlineData(VideoSource.FacebookPhoto)]
    [InlineData(VideoSource.InstagramPhoto)]
    public async Task PostCaption_IsSavedBesideImage_WithExactLineBreaksUnicodeAndLinks(
        VideoSource source
    )
    {
        const string caption =
            "No hot water. Not even luke warm\nA full post, not image alt text.\nខ្មែរ 😊 https://example.test/?a=1&b=2";
        await MediaTextFile.SaveAsync(MediaPath, Photo(caption) with { Source = source });
        Assert.Equal(caption, await File.ReadAllTextAsync(TextPath));
        Assert.Equal(Encoding.UTF8.GetBytes(caption), await File.ReadAllBytesAsync(TextPath));
    }

    [Fact]
    public async Task OriginalCaption_IsPreserved_WhenTitleWasTranslated()
    {
        await MediaTextFile.SaveAsync(
            MediaPath,
            Photo("Original post caption"),
            "Translated filename title"
        );
        Assert.Equal("Original post caption", await File.ReadAllTextAsync(TextPath));
    }

    [Theory]
    [InlineData(VideoSource.FacebookPhoto)]
    [InlineData(VideoSource.InstagramPhoto)]
    public async Task CaptionlessPhoto_DoesNotSaveImageAltOrGeneratedFilenameAsCaption(
        VideoSource source
    )
    {
        await MediaTextFile.SaveAsync(MediaPath, Photo(string.Empty) with { Source = source });
        Assert.False(File.Exists(TextPath));
    }

    [Fact]
    public async Task ExistingTitleSidecars_KeepTheirTranslatedTitleBehavior()
    {
        await MediaTextFile.SaveAsync(
            MediaPath,
            Photo(null) with
            {
                Source = VideoSource.YouTube,
            },
            "Translated video title"
        );
        Assert.Equal("Translated video title", await File.ReadAllTextAsync(TextPath));
    }

    [Fact]
    public async Task CancelledDownload_DoesNotWriteCaption()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MediaTextFile.SaveAsync(
                MediaPath,
                Photo("Caption"),
                cancellationToken: cancellation.Token
            )
        );
        Assert.False(File.Exists(TextPath));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
