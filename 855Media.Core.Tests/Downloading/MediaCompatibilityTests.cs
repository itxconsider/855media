using System.Diagnostics;
using _855Media.Core.Downloading;
using _855Media.Core.Utils;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class MediaCompatibilityTests
{
    private const string H264Video =
        "Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'video.mp4':\nStream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1920x1080";

    [Theory]
    [InlineData("")]
    [InlineData("Stream #0:1[0x2](eng): Audio: aac (LC) (mp4a / 0x6134706D), 48000 Hz, stereo")]
    public void CompatibleOrSilentVideo_DoesNotNeedRewriting(string audio)
    {
        var codecs = MediaCompatibility.ParseStreamCodecs(H264Video + "\n" + audio);
        Assert.NotNull(codecs);
        Assert.True(codecs.Value.CanKeepOriginal);
    }

    [Fact]
    public void OpusAudio_ConvertsAudioWithoutEncodingH264Video()
    {
        var codecs = MediaCompatibility.ParseStreamCodecs(
            H264Video + "\nStream #0:1: Audio: opus, 48000 Hz, stereo"
        );
        Assert.NotNull(codecs);
        Assert.True(codecs.Value.CopyVideo);
        Assert.False(codecs.Value.CopyAudio);
        Assert.False(codecs.Value.CanKeepOriginal);
        var args = MediaCompatibility
            .BuildConversionArguments("input.mp4", "output.mp4", codecs.Value, "copy")
            .ToList();
        Assert.Equal("copy", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void MixedAudioTracks_AllMappedTracksMustBeCompatible()
    {
        var codecs = MediaCompatibility.ParseStreamCodecs(
            H264Video
                + "\nStream #0:1: Audio: aac (LC), 48000 Hz\nStream #0:2: Audio: opus, 48000 Hz"
        );
        Assert.NotNull(codecs);
        Assert.False(codecs.Value.CopyAudio);
    }

    [Theory]
    [InlineData("Stream #0:0: Video: av1 (Main), yuv420p, 3840x2160")]
    [InlineData("Stream #0:0: Video: h264 (High 10), yuv420p10le, 1920x1080")]
    [InlineData("Stream #0:0: Video: h264 (High 4:4:4 Predictive), yuv444p, 1920x1080")]
    public void IncompatibleVideo_RequiresEncodingButKeepsAac(string video)
    {
        var codecs = MediaCompatibility.ParseStreamCodecs(
            video + "\nStream #0:1: Audio: aac (LC), 48000 Hz"
        );
        Assert.NotNull(codecs);
        Assert.False(codecs.Value.CopyVideo);
        Assert.True(codecs.Value.CopyAudio);
        var args = MediaCompatibility
            .BuildConversionArguments("input.mp4", "output.mp4", codecs.Value, "libx264")
            .ToList();
        Assert.Equal("copy", args[args.IndexOf("-c:a") + 1]);
        Assert.InRange(int.Parse(args[args.IndexOf("-threads") + 1]), 1, 2);
        Assert.InRange(int.Parse(args[args.IndexOf("-threads:v") + 1]), 1, 2);
    }

    [Fact]
    public void CoverArtBeforeMainVideo_MapsMainVideoAndOnlyRemuxes()
    {
        var codecs = MediaCompatibility.ParseStreamCodecs(
            "Stream #0:0: Video: mjpeg, yuvj420p, 600x600 (attached pic)\n"
                + H264Video.Replace("#0:0", "#0:2")
        );
        Assert.NotNull(codecs);
        Assert.Equal(2, codecs.Value.VideoIndex);
        Assert.True(codecs.Value.CopyVideo);
        Assert.True(codecs.Value.HasAuxiliaryVideo);
        Assert.False(codecs.Value.CanKeepOriginal);
    }

    [Theory]
    [InlineData("input.mp4: Invalid data found when processing input")]
    [InlineData("Stream #0:0: Video: mjpeg, yuvj420p, 600x600 (attached pic)")]
    public void UnusableProbe_CannotVerifyH264Output(string diagnostic)
    {
        Assert.Null(MediaCompatibility.ParseStreamCodecs(diagnostic));
    }

    [Theory]
    [InlineData("video.webm")]
    [InlineData("video.mkv")]
    public async Task VideoOutput_RejectsContainersThatBypassMp4Compatibility(string path)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MediaCompatibility.EnsureWindowsCompatibleAsync(path)
        );
        Assert.Contains("MP4", error.Message);
    }

    [Fact]
    public async Task MissingFFmpeg_FailsInsteadOfAcceptingUnverifiedVideo()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MediaCompatibility.EnsureWindowsCompatibleAsync(
                "video.mp4",
                Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")
            )
        );
        Assert.Contains("FFmpeg is required", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidProbeOrEmptyDownload_FailsAndPreservesOriginal(bool empty)
    {
        var directory = Path.Combine(Path.GetTempPath(), "855media-h264-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "video.mp4");
        var fakeFFmpeg = Path.Combine(directory, "ffmpeg.exe");
        var original = empty ? Array.Empty<byte>() : new byte[] { 1, 2, 3, 4 };
        try
        {
            await File.WriteAllBytesAsync(path, original);
            // A non-executable file exercises probe failure without starting a child process.
            await File.WriteAllTextAsync(fakeFFmpeg, "invalid executable fixture");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MediaCompatibility.EnsureWindowsCompatibleAsync(path, fakeFFmpeg)
            );
            Assert.Contains(empty ? "missing or empty" : "Cannot verify", error.Message);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".transcoded.mp4"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(fakeFFmpeg);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void SavedWebMPreference_UsesMp4VideoWithoutChoosingAudioOnlyOption()
    {
        var options = new[]
        {
            new VideoDownloadOption(YoutubeExplode.Videos.Streams.Container.Mp4, true, []),
            new VideoDownloadOption(
                YoutubeExplode.Videos.Streams.Container.Mp4,
                false,
                [],
                new YoutubeExplode.Videos.Streams.VideoQuality(720, 30)
            ),
        };
        var preference = new VideoDownloadPreference(
            YoutubeExplode.Videos.Streams.Container.WebM,
            VideoQualityPreference.Highest
        );
        var selected = preference.TryGetBestOption(options);
        Assert.NotNull(selected);
        Assert.False(selected.IsAudioOnly);
        Assert.Equal(YoutubeExplode.Videos.Streams.Container.Mp4, selected.Container);
    }

    [Fact]
    public void AudioOnlyMp4Option_DoesNotAcquireVideoConversionFlags()
    {
        var option = new VideoDownloadOption(YoutubeExplode.Videos.Streams.Container.Mp4, true, []);
        var args = VideoDownloader.BuildYtDlpArguments("audio.mp4", "dQw4w9WgXcQ", option, null);
        Assert.Contains("bestaudio/best", args);
        Assert.DoesNotContain("--remux-video", args);
    }

    [Fact]
    public async Task Cancellation_TerminatesRunningProcessBeforeReturning()
    {
        using var process = new Process();
        process.StartInfo.FileName = OperatingSystem.IsWindows() ? "ping.exe" : "sleep";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        if (OperatingSystem.IsWindows())
        {
            process.StartInfo.ArgumentList.Add("-n");
            process.StartInfo.ArgumentList.Add("30");
            process.StartInfo.ArgumentList.Add("127.0.0.1");
        }
        else
        {
            process.StartInfo.ArgumentList.Add("30");
        }
        process.Start();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var wait = process.WaitForExitWithCancellationAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
            Assert.True(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void YouTubeMp4_UsesRemuxBeforeSingleCompatibilityConversion(bool allowAnyFormat)
    {
        var option = new VideoDownloadOption(
            YoutubeExplode.Videos.Streams.Container.Mp4,
            false,
            []
        );
        var args = VideoDownloader.BuildYtDlpArguments(
            "video.mp4",
            "dQw4w9WgXcQ",
            option,
            null,
            allowAnyFormat: allowAnyFormat
        );
        Assert.Contains("--remux-video", args);
        Assert.DoesNotContain("--recode-video", args);
        var outputIndex = args.ToList().IndexOf("-o");
        Assert.Equal("video.%(ext)s", args[outputIndex + 1]);
    }

    [Fact]
    public void H264InMislabeledMatroska_RequiresMp4Remux()
    {
        var diagnostic = H264Video.Replace("mov,mp4,m4a,3gp,3g2,mj2", "matroska,webm");
        var codecs = MediaCompatibility.ParseStreamCodecs(diagnostic);
        Assert.NotNull(codecs);
        Assert.True(codecs.Value.CopyVideo);
        Assert.False(codecs.Value.IsMp4);
        Assert.False(codecs.Value.CanKeepOriginal);
    }

    [Fact]
    public void DiagnosticLog_RetainsRecentErrorsWithBoundedMemory()
    {
        var log = new BoundedProcessLog();
        for (var i = 0; i < 10000; i++)
            log.Add($"{i:D5}:" + new string('x', 5000));
        var lines = log.ToString().Split(Environment.NewLine);
        Assert.Equal(128, lines.Length);
        Assert.All(lines, line => Assert.Equal(4096, line.Length));
        Assert.StartsWith("09872:", lines[0]);
        Assert.StartsWith("09999:", lines[^1]);
    }
}
