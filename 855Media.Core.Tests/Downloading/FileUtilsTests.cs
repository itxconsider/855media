using System;
using System.IO;
using System.Threading.Tasks;
using _855Media.Core.Utils;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class FileUtilsTests
{
    [Fact]
    public async Task ReplaceFileWithRetryAsync_AtomicallyOverwritesExistingDestination()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var destPath = Path.Combine(tempDir, "video.mp4");
            var sourcePath = Path.Combine(tempDir, "video.mp4.transcoded.mp4");

            await File.WriteAllTextAsync(destPath, "original content");
            await File.WriteAllTextAsync(sourcePath, "new transcoded content");

            await FileUtils.ReplaceFileWithRetryAsync(sourcePath, destPath);

            Assert.True(File.Exists(destPath));
            Assert.False(File.Exists(sourcePath));
            var content = await File.ReadAllTextAsync(destPath);
            Assert.Equal("new transcoded content", content);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    [Fact]
    public async Task TryDeleteWithRetryAsync_CleansUpFileSafely()
    {
        var tempFile = Path.GetTempFileName();
        Assert.True(File.Exists(tempFile));

        await FileUtils.TryDeleteWithRetryAsync(tempFile);
        Assert.False(File.Exists(tempFile));
    }
}
