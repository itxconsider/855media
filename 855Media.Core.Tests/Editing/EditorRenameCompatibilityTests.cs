using System.Text.Json;
using System.Text.Json.Serialization;
using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class EditorRenameCompatibilityTests
{
    [Fact]
    public void SavedJobRetainsLegacyKeysAndEnumValues()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        const string legacy = """
            {"FilePath":"source.mp4","EnableSplitAndUpscale":true,"MergeAfterUpscale":false,
             "DeleteSourceAfterUpscale":true,"Status":"Paused","ModelType":3,"Codec":"H264"}
            """;
        var job = JsonSerializer.Deserialize<EditJob>(legacy, options)!;
        Assert.True(job.EnableSplitAndEdit);
        Assert.False(job.MergeAfterEdit);
        Assert.True(job.DeleteSourceAfterEdit);
        Assert.Equal(EditJobStatus.Paused, job.Status);
        Assert.Equal(EditModelType.FastNative, job.ModelType);
        using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(job, options));
        Assert.True(roundTrip.RootElement.GetProperty("EnableSplitAndUpscale").GetBoolean());
        Assert.False(roundTrip.RootElement.TryGetProperty("EnableSplitAndEdit", out _));
    }
}
