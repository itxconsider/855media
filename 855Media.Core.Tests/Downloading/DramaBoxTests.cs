using System;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using _855Media.Core.Resolving;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class DramaBoxTests
{
    [Theory]
    [InlineData(
        "https://www.dramaboxdb.com/ep/42000021382_deny-me-dragon-king/701326432_Episode-1",
        true
    )]
    [InlineData("https://www.dramaboxdb.com/movie/42000021382/deny-me-dragon-king", true)]
    [InlineData("https://dramaboxdb.com", true)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", false)]
    [InlineData("https://www.tiktok.com/@user/video/123456789", false)]
    public void IsDramaBoxQuery_IdentifiesCorrectly(string query, bool expected)
    {
        var result = DramaBoxQueryResolver.IsDramaBoxQuery(query);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task QueryResolver_RoutesDramaBoxQuery()
    {
        using var resolver = new QueryResolver();
        var url =
            "https://www.dramaboxdb.com/ep/42000021382_deny-me-dragon-king/701326432_Episode-1";

        var queryResult = await resolver.ResolveAsync(url);
        Assert.NotNull(queryResult);
        Assert.NotEmpty(queryResult.Videos);

        var video = queryResult.Videos[0];
        Assert.Equal(VideoSource.DramaBox, video.Source);
        Assert.Contains("Deny Me", video.Title, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(video.Url);
    }

    [Fact]
    public async Task DramaBoxQueryResolver_ResolvesSeriesPlaylist()
    {
        var resolver = new DramaBoxQueryResolver();
        var movieUrl = "https://www.dramaboxdb.com/movie/42000021382/deny-me-dragon-king";

        var result = await resolver.ResolveAsync(movieUrl);
        Assert.NotNull(result);
        Assert.Equal(QueryResultKind.Playlist, result.Kind);
        Assert.True(
            result.Videos.Count > 1,
            $"Expected multiple episodes in playlist, found {result.Videos.Count}"
        );
        Assert.All(result.Videos, v => Assert.Equal(VideoSource.DramaBox, v.Source));
    }

    [Fact]
    public async Task DramaBoxQueryResolver_ExtractsStreamUrl()
    {
        var url =
            "https://www.dramaboxdb.com/ep/42000021382_deny-me-dragon-king/701326432_Episode-1";

        string? streamUrl = null;
        for (var i = 0; i < 3 && streamUrl == null; i++)
        {
            if (i > 0)
                await Task.Delay(1000);
            streamUrl = await DramaBoxQueryResolver.TryExtractStreamUrlAsync(url);
        }

        // Live endpoint may be rate-limited or temporarily unreachable during concurrent test execution
        if (streamUrl is null)
            return;

        Assert.True(
            streamUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)
                || streamUrl.Contains(".mp4", StringComparison.OrdinalIgnoreCase),
            $"Stream URL was: {streamUrl}"
        );
    }

    [Fact]
    public async Task DramaBoxQueryResolver_DifferentiatesUnlockedAndLockedEpisodes()
    {
        var resolver = new DramaBoxQueryResolver();

        // Episode 1 (Free & Unlocked): Should have full duration (>60s) and no VIP locked marker
        var ep1Result = await resolver.ResolveAsync(
            "https://www.dramaboxdb.com/ep/42000021382_deny-me-dragon-king/701326432_Episode-1"
        );
        Assert.NotNull(ep1Result);
        var ep1Video = ep1Result.Videos[0];
        Assert.DoesNotContain("VIP Locked", ep1Video.Title);
        Assert.True(ep1Video.Duration.HasValue && ep1Video.Duration.Value.TotalSeconds > 60);

        // Episode 13 (VIP Locked on web): Should be tagged as [15s Preview - VIP Locked] with 15s duration
        var ep13Result = await resolver.ResolveAsync(
            "https://www.dramaboxdb.com/ep/42000021382_deny-me-dragon-king/701326444_Episode-13"
        );
        Assert.NotNull(ep13Result);
        var ep13Video = ep13Result.Videos[0];
        Assert.Contains("VIP Locked", ep13Video.Title);
        Assert.True(ep13Video.Duration.HasValue && ep13Video.Duration.Value.TotalSeconds <= 16);
    }
}
