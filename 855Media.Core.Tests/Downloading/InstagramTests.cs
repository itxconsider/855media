using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Resolving;
using Xunit;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Tests.Downloading;

public class InstagramTests
{
    [Theory]
    [InlineData("https://www.instagram.com/reel/abc/", true, null)]
    [InlineData("instagram.com/creator/reels/", true, "creator")]
    [InlineData("https://www.instagram.com/nample/reels/", true, "nample")]
    [InlineData("https://instagram.com/creator/?hl=en", true, "creator")]
    [InlineData("https://www.instagram.com/p/abc/", true, null)]
    [InlineData("https://www.instagram.com/reel/abc/?igsh=shared", true, null)]
    [InlineData("https://instagram.com/reels/", true, null)]
    [InlineData("https://instagram.com/stories/creator/123/", true, null)]
    [InlineData("https://notinstagram.com/creator", false, null)]
    [InlineData("https://instagram.com.evil.test/creator", false, null)]
    [InlineData("https://example.test/?url=instagram.com", false, null)]
    public void Queries_AreRoutedByHostAndPath(string query, bool expected, string? username)
    {
        Assert.Equal(expected, InstagramQueryResolver.IsInstagramQuery(query));
        Assert.Equal(username, InstagramQueryResolver.GetProfileUsername(query));
    }

    private static Cookie[] Cookies => [new("sessionid", "fixture", "/", ".instagram.com")];

    private const string Profile = """
        {"data":{"user":{"id":"123","username":"creator","full_name":"Creator","profile_pic_url":"https://example.test/avatar.jpg"}}}
        """;
    private const string Item = """
        {"pk":"100","code":"POST","caption":{"text":"Caption"},"video_duration":5,"video_versions":[{"url":"https://example.test/video.mp4"}]}
        """;

    [Theory]
    [InlineData("reel")]
    [InlineData("p")]
    [InlineData("tv")]
    public void SingleVideo_KeepsReelUrlCaptionAndDownloadQuality(string path)
    {
        var url = $"https://www.instagram.com/{path}/CLIP/?igsh=shared";
        var json = JsonSerializer.Serialize(
            new
            {
                id = "clip",
                webpage_url = url,
                title = "Reel by creator",
                description = "Original caption\nSecond line",
                uploader = "Creator",
                duration = 12.5,
                formats = new[] { new { vcodec = "h264", height = 1080 } },
            }
        );
        var result = InstagramQueryResolver.ParsePost(json);
        var video = Assert.Single(result.Videos);
        Assert.Equal(QueryResultKind.Video, result.Kind);
        Assert.Equal(VideoSource.Instagram, video.Source);
        Assert.Equal(url, video.Url);
        Assert.Equal("Original caption\nSecond line", video.Title);
        Assert.Equal(TimeSpan.FromSeconds(12.5), video.Duration);
        Assert.Null(video.PlaylistIndex);
        var args = InstagramDownloader.BuildArguments("clip.mp4", video, Container.Mp4);
        Assert.Contains("--no-playlist", args);
        Assert.DoesNotContain("--playlist-items", args);
        Assert.Equal("mp4", args[args.IndexOf("--remux-video") + 1]);
        Assert.Equal("bestvideo*+bestaudio/best", args[args.IndexOf("--format") + 1]);
        Assert.Equal(url, args.Last());
    }

    [Fact]
    public async Task Profile_FollowsAllPagesAndDeduplicatesVideos()
    {
        var urls = new List<string>();
        using var handler = new FixtureHandler(
            (request, index) =>
            {
                urls.Add(request.RequestUri!.AbsoluteUri);
                Assert.Contains("sessionid=fixture", request.Headers.GetValues("Cookie").Single());
                return index switch
                {
                    0 => Profile,
                    1 => "{\"items\":["
                        + Item
                        + "],\"more_available\":true,\"next_max_id\":\"cursor+1\"}",
                    _ => "{\"items\":["
                        + Item
                        + ","
                        + Item.Replace("100", "101").Replace("POST", "SECOND")
                        + "],\"more_available\":false}",
                };
            }
        );
        using var client = new HttpClient(handler);
        var result = await new InstagramQueryResolver(Cookies, client).ResolveAsync(
            "https://instagram.com/creator/"
        );
        Assert.Equal(QueryResultKind.Channel, result.Kind);
        Assert.Equal(2, result.Videos.Count);
        Assert.Equal("Caption", result.Videos[0].Title);
        Assert.Equal(VideoSource.Instagram, result.Videos[0].Source);
        Assert.Contains("max_id=cursor%2B1", urls[2]);
    }

    [Fact]
    public async Task Profile_RepeatedCursorFailsInsteadOfReturningIncompleteSuccess()
    {
        using var handler = new FixtureHandler(
            (_, index) =>
                index == 0
                    ? Profile
                    : "{\"items\":[" + Item + "],\"more_available\":true,\"next_max_id\":\"same\"}"
        );
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InstagramQueryResolver(Cookies, client).ResolveAsync(
                "https://instagram.com/creator/"
            )
        );
        Assert.Contains("incomplete", exception.Message);
    }

    [Fact]
    public async Task Profile_MissingPaginationStatusCannotSilentlyTruncate()
    {
        using var handler = new FixtureHandler(
            (_, index) => index == 0 ? Profile : "{\"items\":[" + Item + "]}"
        );
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InstagramQueryResolver(Cookies, client).ResolveAsync(
                "https://instagram.com/creator/"
            )
        );
        Assert.Contains("incomplete", exception.Message);
    }

    [Fact]
    public async Task Profile_LoginPageGetsActionableError()
    {
        using var handler = new FixtureHandler((_, _) => "<html>Login required</html>");
        using var client = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InstagramQueryResolver(Cookies, client).ResolveAsync(
                "https://instagram.com/creator/"
            )
        );
        Assert.Contains("cookies", exception.Message);
    }

    [Fact]
    public void FeedCarousel_SkipsPhotosAndKeepsActualVideoPosition()
    {
        using var item = JsonDocument.Parse(
            """
            {"pk":"post","code":"CAROUSEL","carousel_media":[
              {"pk":"photo","media_type":1},
              {"pk":"video","video_versions":[{"url":"https://example.test/video.mp4"}]}]}
            """
        );
        var video = Assert.Single(
            InstagramQueryResolver.ParseFeedItem(
                item.RootElement,
                "creator",
                "Creator",
                InstagramMediaKind.Videos
            )
        );
        Assert.Equal(2, video.PlaylistIndex);
        var args = InstagramDownloader.BuildArguments(
            "output.mp4",
            video,
            Container.Mp4,
            cookiePath: "cookies.txt"
        );
        Assert.Equal("2", args[args.IndexOf("--playlist-items") + 1]);
        Assert.DoesNotContain("--no-playlist", args);
        Assert.Equal("https://www.instagram.com/p/CAROUSEL/", args.Last());
        Assert.Equal("cookies.txt", args[args.IndexOf("--cookies") + 1]);
    }

    [Fact]
    public void PostCarousel_PreservesPositionsAfterSkippingPhotos()
    {
        var result = InstagramQueryResolver.ParsePost(
            """
            {"title":"Post","webpage_url":"https://www.instagram.com/p/CAROUSEL/","entries":[
              {"id":"photo","formats":[]},
              {"id":"video","title":"Clip","formats":[{"vcodec":"h264"}]}]}
            """
        );
        var video = Assert.Single(result.Videos);
        Assert.Equal(2, video.PlaylistIndex);
        Assert.Equal("video", video.Id);
        Assert.Equal("https://www.instagram.com/p/CAROUSEL/", video.Url);
    }

    [Fact]
    public void PhotoOnlyPost_ExplainsWhyItCannotDownloadVideo()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            InstagramQueryResolver.ParsePost(
                """
                {"id":"photo","webpage_url":"https://www.instagram.com/p/PHOTO/","formats":[]}
                """
            )
        );
        Assert.Contains("Photo-only", ex.Message);
    }

    private const string Photo = """
        {"pk":"photo1","code":"BA","media_type":1,"caption":{"text":"Photo caption\nខ្មែរ"},
        "image_versions2":{"candidates":[
        {"url":"https://example.test/cropped.jpg","width":206,"height":206},
        {"url":"https://example.test/full.jpg?token=signed%2Bvalue&size=original","width":1536,"height":2048},
        {"url":"https://example.test/preview.jpg","width":640,"height":853}]}}
        """;

    [Fact]
    public async Task PhotoPost_UsesSignedLargestImageAndPreservesCaption()
    {
        using var handler = new FixtureHandler(
            (request, _) =>
            {
                Assert.Equal("/api/v1/media/64/info/", request.RequestUri!.AbsolutePath);
                return "{\"items\":[" + Photo + "]}";
            }
        );
        using var client = new HttpClient(handler);
        var result = await new InstagramQueryResolver(
            Cookies,
            client,
            InstagramMediaKind.Photos
        ).ResolveAsync("https://www.instagram.com/p/BA/?img_index=1");
        var photo = Assert.Single(result.Videos);
        Assert.True(photo.IsPhoto);
        Assert.Equal(VideoSource.InstagramPhoto, photo.Source);
        Assert.Equal("Photo caption\nខ្មែរ", photo.Caption);
        Assert.Equal(
            "https://example.test/full.jpg?token=signed%2Bvalue&size=original",
            Assert.Single(photo.ThumbnailUrls)
        );
        Assert.Equal("https://www.instagram.com/p/BA/", photo.Url);
        Assert.Null(photo.Duration);
    }

    [Fact]
    public async Task ProfilePhotos_FollowsPagesAndDoesNotIncludeVideoCovers()
    {
        using var handler = new FixtureHandler(
            (_, index) =>
                index switch
                {
                    0 => Profile,
                    1 => "{\"items\":["
                        + Photo
                        + ","
                        + Item
                        + "],\"more_available\":true,\"next_max_id\":\"next\"}",
                    _ => "{\"items\":["
                        + Photo
                        + ","
                        + Photo.Replace("photo1", "photo2").Replace("BA", "BB")
                        + "],\"more_available\":false}",
                }
        );
        using var client = new HttpClient(handler);
        var result = await new InstagramQueryResolver(
            Cookies,
            client,
            InstagramMediaKind.Photos
        ).ResolveAsync("https://www.instagram.com/nample/");
        Assert.Equal(2, result.Videos.Count);
        Assert.All(result.Videos, photo => Assert.True(photo.IsPhoto));
        Assert.Equal(new[] { "photo1", "photo2" }, result.Videos.Select(p => p.Id));
    }

    [Theory]
    [InlineData(InstagramMediaKind.All, 2)]
    [InlineData(InstagramMediaKind.Photos, 1)]
    [InlineData(InstagramMediaKind.Videos, 1)]
    public void MixedCarousel_FiltersWithoutChangingMediaPositions(
        InstagramMediaKind kind,
        int count
    )
    {
        using var item = JsonDocument.Parse(
            "{\"code\":\"BA\",\"caption\":{\"text\":\"Shared caption\"},\"carousel_media\":["
                + Photo
                + ","
                + Item
                + "]}"
        );
        var media = InstagramQueryResolver
            .ParseFeedItem(item.RootElement, "creator", "Creator", kind)
            .ToArray();
        Assert.Equal(count, media.Length);
        if (kind != InstagramMediaKind.Videos)
            Assert.Equal(1, media.Single(p => p.IsPhoto).PlaylistIndex);
        if (kind != InstagramMediaKind.Photos)
            Assert.Equal(2, media.Single(p => !p.IsPhoto).PlaylistIndex);
        Assert.All(media, p => Assert.Equal("Shared caption", p.Caption));
    }

    [Fact]
    public void PhotoMissingImage_FailsWithoutSavingAPreviewOrPostHtml()
    {
        using var item = JsonDocument.Parse("{\"code\":\"BA\",\"media_type\":1}");
        Assert.Throws<InvalidOperationException>(() =>
            InstagramQueryResolver.ParseFeedItem(item.RootElement, "creator", "Creator").ToArray()
        );
    }

    [Theory]
    [InlineData("BA", "64")]
    [InlineData("aye83DjauH", "482584233761418119")]
    public void Shortcode_MapsToMediaId(string code, string id) =>
        Assert.Equal(id, InstagramQueryResolver.ShortcodeToMediaId(code));

    [Fact]
    public async Task ReelsTab_UsesDedicatedListingAndDeduplicatesAcrossPages()
    {
        var requests = new List<string>();
        using var handler = new FixtureHandler(
            (request, _) =>
            {
                requests.Add(request.RequestUri!.AbsolutePath);
                if (requests.Count == 1)
                    return Profile;
                if (request.Method == HttpMethod.Post)
                {
                    Assert.Equal("/graphql/query/", request.RequestUri.AbsolutePath);
                    var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    Assert.Contains("7845543455542541", body);
                    if (requests.Count == 2)
                        return ReelPage("BA", true, "cursor");
                    Assert.Contains("cursor", body);
                    return ReelPage("BA", false, null, "BB");
                }
                Assert.StartsWith("/api/v1/media/", request.RequestUri.AbsolutePath);
                var code = requests.Count == 3 ? "BA" : "BB";
                return "{\"items\":[" + Item.Replace("POST", code).Replace("100", code) + "]}";
            }
        );
        using var client = new HttpClient(handler);
        var result = await new InstagramQueryResolver(
            Cookies,
            client,
            InstagramMediaKind.Videos
        ).ResolveAsync("https://www.instagram.com/nample/reels/");
        Assert.Equal(2, result.Videos.Count);
        Assert.Equal(5, requests.Count);
        Assert.DoesNotContain(requests, path => path.Contains("/feed/"));
        Assert.All(result.Videos, video => Assert.Equal(VideoSource.Instagram, video.Source));
    }

    [Fact]
    public async Task ReelsTab_RepeatedCursorFailsInsteadOfClaimingComplete()
    {
        using var handler = new FixtureHandler(
            (request, index) =>
                index == 0 ? Profile
                : request.Method == HttpMethod.Post ? ReelPage("BA", true, "same")
                : "{\"items\":[" + Item + "]}"
        );
        using var client = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new InstagramQueryResolver(Cookies, client, InstagramMediaKind.Videos).ResolveAsync(
                "https://instagram.com/nample/reels/"
            )
        );
        Assert.Contains("incomplete", ex.Message);
    }

    private static string ReelPage(
        string code,
        bool more,
        string? cursor,
        string? extraCode = null
    ) =>
        JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    xdt_api__v1__clips__user__connection_v2 = new
                    {
                        edges = (
                            extraCode is null ? new[] { code } : new[] { code, extraCode }
                        ).Select(c => new { node = new { media = new { code = c } } }),
                        page_info = new { has_next_page = more, end_cursor = cursor },
                    },
                },
            }
        );

    private sealed class FixtureHandler(Func<HttpRequestMessage, int, string> response)
        : HttpMessageHandler
    {
        private int _index;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        response(request, _index++),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
