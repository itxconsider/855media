using System.Text.Json;
using _855Media.Core.Downloading;
using _855Media.Core.Resolving;
using Xunit;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Tests.Downloading;

public sealed class InstagramBrowserTests
{
    [Theory]
    [InlineData("https://www.instagram.com/p/ABC/", "ABC")]
    [InlineData("https://www.instagram.com/nample/reel/ABC/?igsh=one", "ABC")]
    [InlineData("https://www.instagram.com/nample/reels/", null)]
    [InlineData("https://www.instagram.com/accounts/login/", null)]
    [InlineData("https://instagram.com.evil.test/p/ABC/", null)]
    [InlineData("http://www.instagram.com/p/ABC/", null)]
    public void PostLinks_AreRestrictedToHttpsInstagramPosts(string url, string? code) =>
        Assert.Equal(code, InstagramBrowserCollection.GetPostCode(url));

    private static string Post(
        string photoUrl = "https://scontent.fixture.cdninstagram.com/full.jpg?token=a%2Bb&size=full"
    ) =>
        JsonSerializer.Serialize(
            new
            {
                code = "ABC",
                caption = "Original caption\nខ្មែរ 😊",
                author = "nample",
                items = new[]
                {
                    new { url = photoUrl, isVideo = false },
                    new
                    {
                        url = "https://video.fixture.fbcdn.net/reel.mp4?token=signed",
                        isVideo = true,
                    },
                },
            }
        );

    [Theory]
    [InlineData(InstagramMediaKind.All, 2)]
    [InlineData(InstagramMediaKind.Photos, 1)]
    [InlineData(InstagramMediaKind.Videos, 1)]
    public void BrowserPost_PreservesMediaPositionsCaptionsAndSignedAssets(
        InstagramMediaKind kind,
        int expected
    )
    {
        var media = InstagramBrowserCollection.ParsePost(
            "https://www.instagram.com/p/ABC/",
            Post(),
            kind
        );
        Assert.Equal(expected, media.Count);
        foreach (var item in media)
        {
            Assert.Equal("Original caption\nខ្មែរ 😊", item.Caption);
            Assert.Equal("https://www.instagram.com/p/ABC/", item.Url);
            if (item.IsPhoto)
            {
                Assert.Equal("ABC_1", item.Id);
                Assert.Equal(
                    "https://scontent.fixture.cdninstagram.com/full.jpg?token=a%2Bb&size=full",
                    Assert.Single(item.ThumbnailUrls)
                );
                Assert.Null(item.DirectMediaUrl);
            }
            else
            {
                Assert.Equal("ABC_2", item.Id);
                Assert.Equal(
                    "https://video.fixture.fbcdn.net/reel.mp4?token=signed",
                    item.DirectMediaUrl
                );
            }
        }
    }

    [Theory]
    [InlineData("http://scontent.cdninstagram.com/photo.jpg")]
    [InlineData("https://cdninstagram.com.evil.test/photo.jpg")]
    [InlineData("https://www.instagram.com/accounts/login/")]
    [InlineData("blob:https://www.instagram.com/video")]
    public void InvalidAssets_CannotEnterTheDownloadQueue(string url) =>
        Assert.Throws<InvalidOperationException>(() =>
            InstagramBrowserCollection.ParsePost(
                "https://www.instagram.com/p/ABC/",
                Post(url),
                InstagramMediaKind.All
            )
        );

    [Fact]
    public void NavigationChange_RejectsOtherPosts() =>
        Assert.Throws<InvalidOperationException>(() =>
            InstagramBrowserCollection.ParsePost(
                "https://www.instagram.com/p/DIFFERENT/",
                Post(),
                InstagramMediaKind.All
            )
        );

    [Fact]
    public void BrowserReel_DownloadUsesSignedMediaWithoutPostExtractionOrCarouselSelection()
    {
        var reel = Assert.Single(
            InstagramBrowserCollection.ParsePost(
                "https://www.instagram.com/p/ABC/",
                Post(),
                InstagramMediaKind.Videos
            )
        ) with
        {
            PlaylistIndex = 2,
        };
        var args = InstagramDownloader.BuildArguments("reel.mp4", reel, Container.Mp4);
        Assert.Equal(reel.DirectMediaUrl, args.Last());
        Assert.DoesNotContain(reel.Url, args);
        Assert.Contains("--no-playlist", args);
        Assert.DoesNotContain("--playlist-items", args);
        Assert.Equal("mp4", args[args.IndexOf("--remux-video") + 1]);
    }
}
