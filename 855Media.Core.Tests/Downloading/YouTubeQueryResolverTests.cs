using System;
using System.Linq;
using _855Media.Core.Resolving;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class YouTubeQueryResolverTests
{
    [Theory]
    [InlineData("https://www.youtube.com/@Bushcraftimprovisation", true)]
    [InlineData("https://www.youtube.com/@Bushcraftimprovisation/videos", true)]
    [InlineData("https://www.youtube.com/@Bushcraftimprovisation/shorts", true)]
    [InlineData("@Bushcraftimprovisation", true)]
    [InlineData("https://www.youtube.com/channel/UCI9zpG0EGiiMLmaHtQxeyYw", true)]
    [InlineData("https://www.youtube.com/c/ChannelName", true)]
    [InlineData("https://www.youtube.com/user/UserName", true)]
    [InlineData("https://www.youtube.com/playlist?list=PL8dPuuaLjXtN0ge7yDk_UA0ldZJdhwkoV", true)]
    [InlineData("PL8dPuuaLjXtN0ge7yDk_UA0ldZJdhwkoV", true)]
    [InlineData("https://www.youtube.com/watch?v=0-fpyruiGpE", false)]
    [InlineData("0-fpyruiGpE", false)]
    [InlineData("something random", false)]
    public void IsYouTubeChannelOrPlaylistQuery_DetectsExpectedQueries(string query, bool expected)
    {
        var result = YouTubeQueryResolver.IsYouTubeChannelOrPlaylistQuery(query);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("@Bushcraftimprovisation", "https://www.youtube.com/@Bushcraftimprovisation")]
    [InlineData(
        "https://www.youtube.com/@Bushcraftimprovisation/videos",
        "https://www.youtube.com/@Bushcraftimprovisation/videos"
    )]
    [InlineData(
        "https://www.youtube.com/playlist?list=PL8dPuuaLjXtN0ge7yDk_UA0ldZJdhwkoV",
        "https://www.youtube.com/playlist?list=PL8dPuuaLjXtN0ge7yDk_UA0ldZJdhwkoV"
    )]
    public void NormalizeQueryUrl_ReturnsCanonicalUrl(string query, string expected)
    {
        var result = YouTubeQueryResolver.NormalizeQueryUrl(query);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void VideoInfo_HasViewCount_And_SortingByViewCount_WorksProperly()
    {
        var v1 = new VideoInfo(
            VideoSource.YouTube,
            "v1",
            "https://youtube.com/watch?v=v1",
            "Video 1",
            "Author",
            null,
            37000L,
            TimeSpan.FromMinutes(10),
            []
        );

        var v2 = new VideoInfo(
            VideoSource.YouTube,
            "v2",
            "https://youtube.com/watch?v=v2",
            "Video 2",
            "Author",
            null,
            1500000L,
            TimeSpan.FromMinutes(20),
            []
        );

        var v3 = new VideoInfo(
            VideoSource.YouTube,
            "v3",
            "https://youtube.com/watch?v=v3",
            "Video 3",
            "Author",
            null,
            188000L,
            TimeSpan.FromMinutes(15),
            []
        );

        Assert.True(v1.HasViewCount);
        Assert.Equal("37K views", v1.FormattedViewCount);
        Assert.True(v2.HasViewCount);
        Assert.Equal("1.5M views", v2.FormattedViewCount);
        Assert.True(v3.HasViewCount);
        Assert.Equal("188K views", v3.FormattedViewCount);

        var list = new[] { v1, v2, v3 };
        var sortedByViews = list.OrderByDescending(v => v.ViewCount ?? -1).ToArray();

        Assert.Equal("v2", sortedByViews[0].Id);
        Assert.Equal("v3", sortedByViews[1].Id);
        Assert.Equal("v1", sortedByViews[2].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task TryResolveAsync_PopulatesViewCountsAndMetadata()
    {
        var resolver = new YouTubeQueryResolver();
        var result = await resolver.TryResolveAsync(
            "https://www.youtube.com/@Bushcraftimprovisation/videos"
        );

        if (result is null)
            return; // Skip if offline or network throttled

        Assert.Equal(QueryResultKind.Channel, result.Kind);
        Assert.NotEmpty(result.Videos);
        Assert.Contains(result.Videos, v => v.HasViewCount);
        Assert.NotNull(result.ProfilePictureUrl);
    }
}
