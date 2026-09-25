using System;
using System.Collections.Generic;
using _855Media.Core.Resolving;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class QueryResultProfileTests
{
    [Fact]
    public void QueryResult_StoresProfilePictureAndAuthor()
    {
        var video = new VideoInfo(
            VideoSource.TikTok,
            "12345",
            "https://www.tiktok.com/@creator/video/12345",
            "Test Video",
            "Test Creator",
            "https://www.tiktok.com/@creator",
            1000,
            TimeSpan.FromSeconds(30),
            ["https://example.com/thumb.jpg"]
        );

        var queryResult = new QueryResult(
            QueryResultKind.Channel,
            "TikTok: Test Creator",
            [video],
            "https://example.com/avatar.jpg",
            "Test Creator"
        );

        Assert.Equal(QueryResultKind.Channel, queryResult.Kind);
        Assert.Equal("https://example.com/avatar.jpg", queryResult.ProfilePictureUrl);
        Assert.Equal("Test Creator", queryResult.AuthorName);
        Assert.Single(queryResult.Videos);
    }

    [Fact]
    public void QueryResult_Aggregate_PreservesProfilePicture()
    {
        var v1 = new VideoInfo(
            VideoSource.YouTube,
            "1",
            "https://youtube.com/1",
            "V1",
            "Author1",
            null,
            10,
            null,
            []
        );
        var v2 = new VideoInfo(
            VideoSource.YouTube,
            "2",
            "https://youtube.com/2",
            "V2",
            "Author1",
            null,
            20,
            null,
            []
        );

        var q1 = new QueryResult(
            QueryResultKind.Channel,
            "Channel 1",
            [v1],
            "https://example.com/author1_avatar.jpg",
            "Author1"
        );
        var q2 = new QueryResult(QueryResultKind.Video, "Video 2", [v2]);

        var aggregated = QueryResult.Aggregate([q1, q2]);

        Assert.Equal(QueryResultKind.Aggregate, aggregated.Kind);
        Assert.Equal(2, aggregated.Videos.Count);
        Assert.Equal("https://example.com/author1_avatar.jpg", aggregated.ProfilePictureUrl);
        Assert.Equal("Author1", aggregated.AuthorName);
    }

    [Theory]
    [InlineData(1_500_000_000L, "1.5B views")]
    [InlineData(12_400_000L, "12.4M views")]
    [InlineData(9_263L, "9.3K views")]
    [InlineData(850L, "850 views")]
    [InlineData(null, null)]
    public void VideoInfo_FormattedViewCount_WorksCorrectly(long? viewCount, string? expected)
    {
        var video = new VideoInfo(
            VideoSource.TikTok,
            "1",
            "url",
            "title",
            "author",
            null,
            viewCount,
            null,
            []
        );

        Assert.Equal(expected, video.FormattedViewCount);
        Assert.Equal(viewCount is >= 0, video.HasViewCount);
    }
}
