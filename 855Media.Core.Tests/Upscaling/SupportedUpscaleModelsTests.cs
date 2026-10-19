using System.Text.Json;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class SupportedUpscaleModelsTests
{
    [Fact]
    public void OnlySupportedNativeModesAreOffered() =>
        Assert.Equal(
            new[] { UpscaleModelType.NvidiaRtx, UpscaleModelType.FastNative },
            Enum.GetValues<UpscaleModelType>()
        );

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    public void LegacyOrUnknownPersistedModesMigrateToNative(int legacy)
    {
        var job = JsonSerializer.Deserialize<UpscaleJob>($"{{\"ModelType\":{legacy}}}");
        Assert.NotNull(job);
        Assert.Equal(UpscaleModelType.FastNative, job.ModelType);
    }

    [Theory]
    [InlineData(0, UpscaleModelType.NvidiaRtx)]
    [InlineData(3, UpscaleModelType.FastNative)]
    public void SupportedPersistedModesKeepTheirNumericIdentity(
        int value,
        UpscaleModelType expected
    )
    {
        var job = JsonSerializer.Deserialize<UpscaleJob>($"{{\"ModelType\":{value}}}");
        Assert.NotNull(job);
        Assert.Equal(expected, job.ModelType);
    }
}
