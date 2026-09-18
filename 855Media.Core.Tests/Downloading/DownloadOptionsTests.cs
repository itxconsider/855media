using System;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using YoutubeExplode.Videos.Streams;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class DownloadOptionsTests
{
    [Theory]
    [InlineData(2160, 2160)]
    [InlineData(1440, 1440)]
    [InlineData(1080, 1080)]
    [InlineData(1076, 1080)]
    [InlineData(720, 720)]
    [InlineData(718, 720)]
    [InlineData(480, 480)]
    [InlineData(360, 360)]
    [InlineData(240, 240)]
    [InlineData(144, 144)]
    [InlineData(0, 0)]
    public void NormalizeQualityHeight_MapsCorrectly(int rawHeight, int expected)
    {
        var result = YtDlp.NormalizeQualityHeight(rawHeight);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDefaultOptions_ContainsStandardQualitiesAndAudio()
    {
        var options = YtDlp.GetDefaultOptions();
        Assert.NotEmpty(options);

        // Video options should all have non-null VideoQuality
        var videoOptions = options.Where(o => !o.IsAudioOnly).ToArray();
        Assert.True(videoOptions.Length >= 4);
        Assert.All(videoOptions, v => Assert.NotNull(v.VideoQuality));
        Assert.Contains(videoOptions, v => v.VideoQuality?.MaxHeight == 1080);
        Assert.Contains(videoOptions, v => v.VideoQuality?.MaxHeight == 720);

        // Audio options should be present
        var audioOptions = options.Where(o => o.IsAudioOnly).ToArray();
        Assert.NotEmpty(audioOptions);
        Assert.Contains(audioOptions, a => a.Container == Container.Mp3);
    }

    [Fact]
    public void VideoDownloadOption_SupportsCustomVideoQuality()
    {
        var quality = new VideoQuality(720, 30);
        var option = new VideoDownloadOption(Container.Mp4, false, [], quality);

        Assert.NotNull(option.VideoQuality);
        Assert.Equal(720, option.VideoQuality.Value.MaxHeight);
        Assert.Equal("720p", option.VideoQuality.Value.Label);
    }

    [Fact]
    public void VideoDownloadPreference_SelectsCorrectCustomOption()
    {
        var options = new[]
        {
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1080, 30)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(720, 30)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(480, 30)),
            new VideoDownloadOption(Container.Mp3, true, []),
        };

        var pref1080 = new VideoDownloadPreference(Container.Mp4, VideoQualityPreference.UpTo1080p);
        var best1080 = pref1080.TryGetBestOption(options);
        Assert.NotNull(best1080);
        Assert.Equal(1080, best1080.VideoQuality?.MaxHeight);

        var pref720 = new VideoDownloadPreference(Container.Mp4, VideoQualityPreference.UpTo720p);
        var best720 = pref720.TryGetBestOption(options);
        Assert.NotNull(best720);
        Assert.Equal(720, best720.VideoQuality?.MaxHeight);

        var prefAudio = new VideoDownloadPreference(Container.Mp3, VideoQualityPreference.Highest);
        var bestAudio = prefAudio.TryGetBestOption(options);
        Assert.NotNull(bestAudio);
        Assert.True(bestAudio.IsAudioOnly);
        Assert.Equal(Container.Mp3, bestAudio.Container);
    }

    [Fact]
    public async Task GetDownloadOptionsAsync_ReturnsQualitiesForYouTubeVideo()
    {
        using var downloader = new VideoDownloader();
        var options = await downloader.GetDownloadOptionsAsync("eNhtfE2xMDI");

        Assert.NotEmpty(options);
        // Ensure every video option has a valid video quality
        var videoOptions = options.Where(o => !o.IsAudioOnly).ToArray();
        Assert.NotEmpty(videoOptions);
        Assert.All(videoOptions, o =>
        {
            Assert.NotNull(o.VideoQuality);
            Assert.True(o.VideoQuality.Value.MaxHeight > 0);
            Assert.False(string.IsNullOrWhiteSpace(o.VideoQuality.Value.Label));
        });

        // Ensure we have choices (not just a single empty mp4)
        Assert.True(options.Count > 1, $"Expected multiple quality options, got {options.Count}");
    }
}
