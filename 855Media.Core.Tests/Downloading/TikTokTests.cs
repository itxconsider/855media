using System;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Resolving;
using Xunit;
using YoutubeExplode.Videos.Streams;

namespace _855Media.Core.Tests.Downloading;

public class TikTokTests
{
    [Theory]
    [InlineData("https://www.tiktok.com/shortdrama/episode/7684420063587292161/", true)]
    [InlineData("https://www.tiktok.com/shortdrama/episode/7684420063587292161/1", true)]
    [InlineData("https://www.tiktok.com/shortdrama/7684420063587292161", true)]
    [InlineData("https://m.tiktok.com/shortdrama/episode/en/7684420063587292161/2", true)]
    [InlineData("https://www.tiktok.com/@graindrama/video/7684421286151326989", false)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", false)]
    public void IsTikTokShortDramaQuery_IdentifiesCorrectly(string query, bool expected)
    {
        var result = TikTokQueryResolver.IsTikTokShortDramaQuery(query);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://www.tiktok.com/@graindrama/video/7684421286151326989", true)]
    [InlineData("https://www.tiktok.com/shortdrama/episode/7684420063587292161/", true)]
    [InlineData("https://vt.tiktok.com/ZSjabcdef/", true)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", false)]
    public void IsTikTokQuery_IdentifiesCorrectly(string query, bool expected)
    {
        var result = TikTokQueryResolver.IsTikTokQuery(query);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TikTokDownloader_BuildArguments_IncludesApiHostnameExtractorArg()
    {
        var video = new VideoInfo(
            VideoSource.TikTok,
            "7684421286151326989",
            "https://www.tiktok.com/@graindrama/video/7684421286151326989",
            "The Panda Talks Back - EP01",
            "GrainDrama",
            "https://www.tiktok.com/@graindrama",
            1000,
            TimeSpan.FromSeconds(60),
            Array.Empty<string>()
        );

        var args = TikTokDownloader.BuildArguments("test.mp4", video, Container.Mp4);

        var extractorArgIdx = args.IndexOf("--extractor-args");
        Assert.True(extractorArgIdx >= 0, "Expected --extractor-args flag in arguments");
        Assert.True(
            args.Count > extractorArgIdx + 1,
            "Expected argument value after --extractor-args"
        );
        Assert.Contains(
            "tiktok:api_hostname=api22-normal-c-useast2a.tiktokv.com",
            args[extractorArgIdx + 1]
        );
    }

    [Fact]
    public async Task QueryResolver_ResolvesTikTokShortDramaSeries()
    {
        using var resolver = new QueryResolver();
        var url = "https://www.tiktok.com/shortdrama/episode/7684420063587292161/";

        var result = await resolver.ResolveAsync(url);

        Assert.NotNull(result);
        Assert.Equal(QueryResultKind.Playlist, result.Kind);
        Assert.Contains("The Panda Talks Back", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            result.Videos.Count >= 50,
            $"Expected at least 50 episodes, found {result.Videos.Count}"
        );

        Assert.All(
            result.Videos,
            v =>
            {
                Assert.Equal(VideoSource.TikTok, v.Source);
                Assert.NotEmpty(v.Id);
                Assert.NotEmpty(v.Url);
                Assert.Contains("tiktok.com", v.Url);
                Assert.Contains("The Panda Talks Back", v.Title);
            }
        );

        var ep1 = result.Videos.First();
        Assert.Contains("EP01", ep1.Title);
        Assert.NotEmpty(ep1.ThumbnailUrls);
    }

    [Fact]
    public async Task QueryResolver_ResolvesTikTokShortDramaSingleEpisode()
    {
        using var resolver = new QueryResolver();
        var url = "https://www.tiktok.com/shortdrama/episode/7684420063587292161/1";

        var result = await resolver.ResolveAsync(url);

        Assert.NotNull(result);
        Assert.Equal(QueryResultKind.Video, result.Kind);
        Assert.Single(result.Videos);

        var video = result.Videos[0];
        Assert.Equal(VideoSource.TikTok, video.Source);
        Assert.Contains("The Panda Talks Back", video.Title);
        Assert.Contains("EP01", video.Title);
        Assert.Contains("graindrama", video.Url, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(video.ThumbnailUrls);
    }

    [Fact]
    public void CookieUtils_TryFindCookieFile_DiscoversLocalFileInApplicationDirectory()
    {
        var name = "855media-cookie-fixture-" + Guid.NewGuid().ToString("N") + ".txt";
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, name);
        try
        {
            System.IO.File.WriteAllText(path, "# synthetic cookie fixture");
            var cookieFile = _855Media.Core.Utils.CookieUtils.TryFindCookieFile(name);
            Assert.Equal(path, cookieFile);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void CookieUtils_ParseNetscapeCookieFile_ParsesCorrectly()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "855media-cookie-fixture-" + Guid.NewGuid().ToString("N") + ".txt"
        );
        try
        {
            System.IO.File.WriteAllText(
                path,
                "# Netscape HTTP Cookie File\n#HttpOnly_.example.test\tTRUE\t/\tTRUE\t0\tfixture_cookie\tfixture_value\n"
            );
            var cookie = Assert.Single(
                _855Media.Core.Utils.CookieUtils.ParseNetscapeCookieFile(path)
            );
            Assert.Equal("fixture_cookie", cookie.Name);
            Assert.Equal("fixture_value", cookie.Value);
            Assert.Equal("example.test", cookie.Domain);
            Assert.Equal("/", cookie.Path);
            Assert.True(cookie.Secure);
            Assert.True(cookie.HttpOnly);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void TikTokDownloader_BuildArguments_IncludesCookiesWhenProvided()
    {
        var video = new VideoInfo(
            VideoSource.TikTok,
            "7684421286151326989",
            "https://www.tiktok.com/@graindrama/video/7684421286151326989",
            "The Panda Talks Back - EP01",
            "GrainDrama",
            "https://www.tiktok.com/@graindrama",
            1000,
            TimeSpan.FromSeconds(60),
            Array.Empty<string>()
        );

        var args = TikTokDownloader.BuildArguments(
            "test.mp4",
            video,
            Container.Mp4,
            cookieFilePath: "C:\\path\\to\\tiktok_cookies.txt"
        );

        var cookieIdx = args.IndexOf("--cookies");
        Assert.True(cookieIdx >= 0, "Expected --cookies flag in arguments");
        Assert.Equal("C:\\path\\to\\tiktok_cookies.txt", args[cookieIdx + 1]);
    }
}
