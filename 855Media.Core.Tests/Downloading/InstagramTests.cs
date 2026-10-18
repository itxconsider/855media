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
    [InlineData("https://instagram.com/creator/?hl=en", true, "creator")]
    [InlineData("https://www.instagram.com/p/abc/", true, null)]
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
            InstagramQueryResolver.ParseFeedItem(item.RootElement, "creator", "Creator")
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
