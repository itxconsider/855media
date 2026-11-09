using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowQueueContentTests
{
    [Fact]
    public async Task ExcludesPathsAndRenamedCopiesBeforeSelectionButRetainsDistinctEqualSizeFiles()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "queue-content-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "a.mp4");
        var b = Path.Combine(folder, "b.mp4");
        var c = Path.Combine(folder, "c.mp4");
        try
        {
            await File.WriteAllTextAsync(a, "abc");
            File.Copy(a, b);
            await File.WriteAllTextAsync(c, "xyz");
            Assert.Equal(
                new[] { a, c },
                await PFlowQueueMedia.ExcludeReservedContentAsync(new[] { a, b, c, a }, [])
            );
            Assert.Equal(
                new[] { c },
                await PFlowQueueMedia.ExcludeReservedContentAsync(new[] { a, b, c }, new[] { a })
            );
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            File.Delete(c);
            Directory.Delete(folder);
        }
    }
}
