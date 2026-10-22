using System.Text.Json;
using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class SupportedEditModelsTests
{
    [Fact]
    public void OnlySupportedNativeModesAreOffered() =>
        Assert.Equal(
            new[] { EditModelType.NvidiaRtx, EditModelType.FastNative },
            Enum.GetValues<EditModelType>()
        );

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(999)]
    public void LegacyOrUnknownPersistedModesMigrateToNative(int legacy)
    {
        var job = JsonSerializer.Deserialize<EditJob>($"{{\"ModelType\":{legacy}}}");
        Assert.NotNull(job);
        Assert.Equal(EditModelType.FastNative, job.ModelType);
    }

    [Theory]
    [InlineData(0, EditModelType.NvidiaRtx)]
    [InlineData(3, EditModelType.FastNative)]
    public void SupportedPersistedModesKeepTheirNumericIdentity(int value, EditModelType expected)
    {
        var job = JsonSerializer.Deserialize<EditJob>($"{{\"ModelType\":{value}}}");
        Assert.NotNull(job);
        Assert.Equal(expected, job.ModelType);
    }
}
