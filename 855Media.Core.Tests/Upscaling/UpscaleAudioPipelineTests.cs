using _855Media.Core.Upscaling;
using Xunit;
using Xunit.Abstractions;

namespace _855Media.Core.Tests.Upscaling;

public class UpscaleAudioPipelineTests
{
    private readonly ITestOutputHelper _output;

    public UpscaleAudioPipelineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BuildAudioPipeline_CopyOriginal_1xSpeed_ReturnsNoFilterAndDirectCopy()
    {
        var (filter, reencode, isMuted) = VideoUpscaleService.BuildAudioPipeline(
            UpscaleAudioMode.CopyOriginal,
            1.0
        );

        Assert.Null(filter);
        Assert.False(reencode);
        Assert.False(isMuted);
    }

    [Fact]
    public void BuildAudioPipeline_CopyOriginal_CustomSpeed_ReturnsAtempoAndReencode()
    {
        var (filter, reencode, isMuted) = VideoUpscaleService.BuildAudioPipeline(
            UpscaleAudioMode.CopyOriginal,
            1.5
        );

        Assert.NotNull(filter);
        Assert.Contains("atempo=1.5", filter);
        Assert.True(reencode);
        Assert.False(isMuted);
    }

    [Fact]
    public void BuildAudioPipeline_Mute_ReturnsMuteTrue()
    {
        var (filter, reencode, isMuted) = VideoUpscaleService.BuildAudioPipeline(
            UpscaleAudioMode.Mute,
            1.0
        );

        Assert.Null(filter);
        Assert.False(reencode);
        Assert.True(isMuted);
    }

    [Fact]
    public void BuildAudioPipeline_IsolateSpeech_ReturnsSpeechFiltersAndReencode()
    {
        var (filter, reencode, isMuted) = VideoUpscaleService.BuildAudioPipeline(
            UpscaleAudioMode.IsolateSpeech,
            1.0
        );

        Assert.NotNull(filter);
        Assert.Contains("highpass=f=120", filter);
        Assert.Contains("lowpass=f=3400", filter);
        Assert.Contains("afftdn", filter);
        Assert.True(reencode);
        Assert.False(isMuted);
    }

    [Fact]
    public void BuildAudioPipeline_IsolateSpeech_WithSpeed_CombinesFilters()
    {
        var (filter, reencode, isMuted) = VideoUpscaleService.BuildAudioPipeline(
            UpscaleAudioMode.IsolateSpeech,
            1.25
        );

        Assert.NotNull(filter);
        Assert.Contains("highpass=f=120", filter);
        Assert.Contains("atempo=1.25", filter);
        Assert.True(reencode);
        Assert.False(isMuted);
    }

    [Fact]
    public void BuildAudioPipeline_RemoveVocals_ReturnsKaraokeFilter()
    {
        var (filter, reencode, isMuted) = VideoUpscaleService.BuildAudioPipeline(
            UpscaleAudioMode.RemoveVocals,
            1.0
        );

        Assert.NotNull(filter);
        Assert.Contains("pan=stereo", filter);
        Assert.Contains("equalizer", filter);
        Assert.True(reencode);
        Assert.False(isMuted);
    }

    [Fact]
    public void BuildAudioPipeline_AntiCopyrightPitch_AppliesHarmonicDetune()
    {
        var (filter, reencode, isMuted) = VideoUpscaleService.BuildAudioPipeline(
            UpscaleAudioMode.AntiCopyrightPitch,
            1.0
        );

        Assert.NotNull(filter);
        Assert.Contains("asetrate=49920", filter);
        Assert.Contains("aresample=48000", filter);
        Assert.True(reencode);
        Assert.False(isMuted);
    }

    [Fact]
    public void Job_DefaultsToCopyOriginal()
    {
        var job = new UpscaleJob { FilePath = "sample.mp4" };
        Assert.Equal(UpscaleAudioMode.CopyOriginal, job.AudioMode);
    }

    [Fact]
    public void AudioProcessor_SpeechIsolationFilter_IncludesStereoCenterExtraction()
    {
        var filter = _855Media.Core.Audio.AudioProcessor.GetSpeechIsolationFilter();
        Assert.Contains("pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0+0.5*c1", filter);
        Assert.Contains("afftdn", filter);
    }

    [Fact]
    public void BuildAudioSpeedFilter_FormatsCorrectly()
    {
        Assert.Equal(string.Empty, VideoUpscaleService.BuildAudioSpeedFilter(1.0));
        Assert.Equal("atempo=1.25", VideoUpscaleService.BuildAudioSpeedFilter(1.25));
        Assert.Equal("atempo=2.0,atempo=1.25", VideoUpscaleService.BuildAudioSpeedFilter(2.5));
    }

    [Fact(Skip = "Manual end-to-end integration test on local video")]
    public async Task ProcessJob_IsolateSpeech_ExecutesSuccessfully()
    {
        string videoPath =
            @"D:\@HotFly\[010] Amazing cooking deep fried hotdog for fry noodle recipe_original1x.mp4";
        if (!File.Exists(videoPath))
            return;

        var service = new VideoUpscaleService();
        var outputPath =
            @"D:\@HotFly\[010] Amazing cooking deep fried hotdog for fry noodle recipe_speech_isolated.mp4";

        var job = new UpscaleJob
        {
            FilePath = videoPath,
            CustomOutputFilePath = outputPath,
            TargetResolution = UpscaleTargetResolution.Original1x,
            ModelType = UpscaleModelType.FastNative,
            AudioMode = UpscaleAudioMode.IsolateSpeech,
            SpeedMode = RenderSpeedMode.TurboFast,
        };

        await service.ProcessJobAsync(job);
        _output.WriteLine("===== DETAILED LOG =====");
        _output.WriteLine(job.DetailedLog ?? "(null)");
        _output.WriteLine("========================");
        Assert.Contains("[Audio Stem Separation] Successfully extracted neural", job.DetailedLog);
    }
}
