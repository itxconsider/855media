using System;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.Downloading;
using Xunit;
using YoutubeExplode.Videos.Streams;

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
        Assert.Contains(videoOptions, v => v.VideoQuality?.MaxHeight == 2160);
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
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(2160, 60)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1440, 60)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1080, 30)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(720, 30)),
            new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(480, 30)),
            new VideoDownloadOption(Container.Mp3, true, []),
        };

        var pref2160 = new VideoDownloadPreference(Container.Mp4, VideoQualityPreference.UpTo2160p);
        var best2160 = pref2160.TryGetBestOption(options);
        Assert.NotNull(best2160);
        Assert.Equal(2160, best2160.VideoQuality?.MaxHeight);

        var pref1440 = new VideoDownloadPreference(Container.Mp4, VideoQualityPreference.UpTo1440p);
        var best1440 = pref1440.TryGetBestOption(options);
        Assert.NotNull(best1440);
        Assert.Equal(1440, best1440.VideoQuality?.MaxHeight);

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

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public DownloadOptionsTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GetDownloadOptionsAsync_ReturnsQualitiesForYouTubeVideo()
    {
        using var downloader = new VideoDownloader();
        var options = await downloader.GetDownloadOptionsAsync("jNQXAC9IVRw");

        Assert.NotEmpty(options);
        // Ensure every video option has a valid video quality
        var videoOptions = options.Where(o => !o.IsAudioOnly).ToArray();
        Assert.NotEmpty(videoOptions);
        Assert.All(
            videoOptions,
            o =>
            {
                Assert.NotNull(o.VideoQuality);
                Assert.True(o.VideoQuality.Value.MaxHeight > 0);
                Assert.False(string.IsNullOrWhiteSpace(o.VideoQuality.Value.Label));
            }
        );

        // Ensure we have choices (not just a single empty mp4)
        Assert.True(options.Count > 1, $"Expected multiple quality options, got {options.Count}");
    }

    [Fact]
    public void BuildYtDlpArguments_4K_DoesNotRestrictToAvcAndSortsBy2160p()
    {
        var option = new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(2160, 60));

        var args = VideoDownloader.BuildYtDlpArguments(
            "C:\\output.mp4",
            "jNQXAC9IVRw",
            option,
            null
        );

        var formatIndex = args.ToList().IndexOf("--format");
        Assert.True(formatIndex >= 0);
        var formatArg = args[formatIndex + 1];
        Assert.Contains("bestvideo[aspect_ratio<1][width<=2160]", formatArg);
        Assert.Contains("bestvideo[aspect_ratio>=1][height<=2160]", formatArg);
        Assert.DoesNotContain("vcodec^=avc", formatArg);

        var sortIndex = args.ToList().IndexOf("--format-sort");
        Assert.True(sortIndex >= 0);
        var sortArg = args[sortIndex + 1];
        Assert.Equal("res:2160,fps,quality", sortArg);
    }

    [Fact]
    public void BuildYtDlpArguments_2K_DoesNotRestrictToAvcAndSortsBy1440p()
    {
        var option = new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1440, 60));

        var args = VideoDownloader.BuildYtDlpArguments(
            "C:\\output.mp4",
            "jNQXAC9IVRw",
            option,
            null
        );

        var formatIndex = args.ToList().IndexOf("--format");
        Assert.True(formatIndex >= 0);
        var formatArg = args[formatIndex + 1];
        Assert.Contains("bestvideo[aspect_ratio<1][width<=1440]", formatArg);
        Assert.Contains("bestvideo[aspect_ratio>=1][height<=1440]", formatArg);
        Assert.DoesNotContain("vcodec^=avc", formatArg);

        var sortIndex = args.ToList().IndexOf("--format-sort");
        Assert.True(sortIndex >= 0);
        var sortArg = args[sortIndex + 1];
        Assert.Equal("res:1440,fps,quality", sortArg);
    }

    [Fact]
    public void BuildYtDlpArguments_1080p_SortsByTargetResThenPrefersAvc()
    {
        var option = new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1080, 30));

        var args = VideoDownloader.BuildYtDlpArguments(
            "C:\\output.mp4",
            "jNQXAC9IVRw",
            option,
            null
        );

        var formatIndex = args.ToList().IndexOf("--format");
        Assert.True(formatIndex >= 0);
        var formatArg = args[formatIndex + 1];
        Assert.Contains("bestvideo[aspect_ratio<1][width<=1080]", formatArg);
        Assert.Contains("bestvideo[aspect_ratio>=1][height<=1080]", formatArg);

        var sortIndex = args.ToList().IndexOf("--format-sort");
        Assert.True(sortIndex >= 0);
        var sortArg = args[sortIndex + 1];
        Assert.Equal("res:1080,fps,vcodec:avc,acodec:m4a", sortArg);
    }

    [Fact]
    public void VideoDownloadPreference_SupportsTranslateCaptionsToEnglish()
    {
        var prefDefault = new VideoDownloadPreference(
            Container.Mp4,
            VideoQualityPreference.Highest
        );
        Assert.False(prefDefault.TranslateCaptionsToEnglish);

        var prefWithTranslation = new VideoDownloadPreference(
            Container.Mp4,
            VideoQualityPreference.UpTo1080p,
            TranslateCaptionsToEnglish: true
        );
        Assert.True(prefWithTranslation.TranslateCaptionsToEnglish);
    }

    [Fact]
    public void VideoDownloadPreference_SupportsTranslateTitleToEnglish()
    {
        var prefDefault = new VideoDownloadPreference(
            Container.Mp4,
            VideoQualityPreference.Highest
        );
        Assert.False(prefDefault.TranslateTitleToEnglish);

        var prefWithTranslation = new VideoDownloadPreference(
            Container.Mp4,
            VideoQualityPreference.UpTo1080p,
            TranslateCaptionsToEnglish: false,
            TranslateTitleToEnglish: true
        );
        Assert.True(prefWithTranslation.TranslateTitleToEnglish);
    }

    [Fact]
    public void BuildYtDlpArguments_WithTranslateCaptionsToEnglish_IncludesAutoSubsAndSubLangs()
    {
        var option = new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1080, 30));

        var args = VideoDownloader.BuildYtDlpArguments(
            "C:\\video.mp4",
            "jNQXAC9IVRw",
            option,
            null,
            includeSubtitles: true,
            translateCaptionsToEnglish: true
        );

        Assert.Contains("--write-subs", args);
        Assert.Contains("--write-auto-subs", args);
        Assert.Contains("--sub-langs", args);
        var subLangsIndex = args.ToList().IndexOf("--sub-langs");
        Assert.Equal("en.*,en", args[subLangsIndex + 1]);
        Assert.Contains("--embed-subs", args);
        Assert.Contains("--sub-format", args);
    }

    [Fact]
    public void BuildYtDlpArguments_WithStandardSubtitles_DoesNotForceEnglishAutoSubs()
    {
        var option = new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1080, 30));

        var args = VideoDownloader.BuildYtDlpArguments(
            "C:\\video.mp4",
            "jNQXAC9IVRw",
            option,
            null,
            includeSubtitles: true,
            translateCaptionsToEnglish: false
        );

        Assert.Contains("--write-subs", args);
        Assert.DoesNotContain("--write-auto-subs", args);
        Assert.DoesNotContain("--sub-langs", args);
        Assert.Contains("--embed-subs", args);
    }

    [Theory]
    [InlineData(VideoQualityPreference.Lowest, 360)]
    [InlineData(VideoQualityPreference.UpTo360p, 360)]
    [InlineData(VideoQualityPreference.UpTo480p, 480)]
    [InlineData(VideoQualityPreference.UpTo720p, 720)]
    [InlineData(VideoQualityPreference.UpTo1080p, 1080)]
    [InlineData(VideoQualityPreference.UpTo1440p, 1440)]
    [InlineData(VideoQualityPreference.UpTo2160p, 2160)]
    public void VideoQualityPreference_GetMaxHeight_ReturnsExpectedValue(
        VideoQualityPreference preference,
        int expected
    )
    {
        Assert.Equal(expected, preference.GetMaxHeight());
    }

    [Fact]
    public void VideoQualityPreference_Highest_ReturnsNullMaxHeight()
    {
        Assert.Null(VideoQualityPreference.Highest.GetMaxHeight());
    }

    private static _855Media.Core.Resolving.VideoInfo CreateSampleTikTokVideo() =>
        new(
            _855Media.Core.Resolving.VideoSource.TikTok,
            "123",
            "https://www.tiktok.com/@user/video/123",
            "Test Video",
            "Author",
            null,
            1000L,
            TimeSpan.FromSeconds(60),
            []
        );

    [Fact]
    public void TikTokDownloader_BuildArguments_Portrait1080p_IncludesWidthAndPortraitFilter()
    {
        var video = CreateSampleTikTokVideo();
        var option = new VideoDownloadOption(Container.Mp4, false, [], new VideoQuality(1080, 30));

        var args = TikTokDownloader.BuildArguments("C:\\output.mp4", video, Container.Mp4, option);

        Assert.Contains("--format", args);
        var formatIdx = args.IndexOf("--format");
        var formatString = args[formatIdx + 1];

        // Must support portrait bounding by width
        Assert.Contains("aspect_ratio<1][width<=1080", formatString);
        // Must support landscape bounding by height
        Assert.Contains("aspect_ratio>=1][height<=1080", formatString);
        // Must sort by resolution first so 1080p is selected over lower res H.264
        Assert.Contains("--format-sort", args);
        var sortIdx = args.IndexOf("--format-sort");
        var sortString = args[sortIdx + 1];
        Assert.StartsWith("res:1080", sortString);
    }

    [Fact]
    public void TikTokDownloader_BuildArguments_BatchWithPreference1080p_ResolvesCorrectQuality()
    {
        var video = CreateSampleTikTokVideo();
        var preference = new VideoDownloadPreference(
            Container.Mp4,
            VideoQualityPreference.UpTo1080p
        );

        var args = TikTokDownloader.BuildArguments(
            "C:\\output.mp4",
            video,
            Container.Mp4,
            downloadOption: null,
            downloadPreference: preference
        );

        Assert.Contains("--format", args);
        var formatIdx = args.IndexOf("--format");
        var formatString = args[formatIdx + 1];

        Assert.Contains("aspect_ratio<1][width<=1080", formatString);
        Assert.Contains("bestvideo+bestaudio/best", formatString);

        var sortIdx = args.IndexOf("--format-sort");
        Assert.StartsWith("res:1080", args[sortIdx + 1]);
    }

    [Fact]
    public void TikTokDownloader_BuildArguments_HighestQuality_UsesBestWithoutRestriction()
    {
        var video = CreateSampleTikTokVideo();
        var preference = new VideoDownloadPreference(Container.Mp4, VideoQualityPreference.Highest);

        var args = TikTokDownloader.BuildArguments(
            "C:\\output.mp4",
            video,
            Container.Mp4,
            downloadOption: null,
            downloadPreference: preference
        );

        Assert.Contains("--format", args);
        var formatIdx = args.IndexOf("--format");
        Assert.Equal("bestvideo+bestaudio/best", args[formatIdx + 1]);

        var sortIdx = args.IndexOf("--format-sort");
        Assert.Equal("res,fps,vcodec:h264,quality", args[sortIdx + 1]);
    }
}
