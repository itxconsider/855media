using System.IO.Compression;
using _855Media.Core.Downloading;
using Xunit;

namespace _855Media.Core.Tests;

public class FFprobeSetupTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    public void ExtractionRequiresOneNonEmptyProbeAndPreservesExistingFileOnRejection(string kind)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = Path.Combine(directory, "ffprobe.exe");
            File.WriteAllText(output, "original");
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                if (kind != "missing")
                {
                    using (
                        var writer = new StreamWriter(
                            zip.CreateEntry("build/bin/ffprobe.exe").Open()
                        )
                    )
                        if (kind != "empty")
                            writer.Write("probe");
                    if (kind == "duplicate")
                        using (
                            var writer = new StreamWriter(
                                zip.CreateEntry("other/bin/ffprobe.exe").Open()
                            )
                        )
                            writer.Write("second");
                }
                using var ignored = new StreamWriter(zip.CreateEntry("../../escape.txt").Open());
                ignored.Write("do not extract");
            }
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (kind == "valid")
            {
                FFprobe.ExtractExecutable(archive, output);
                Assert.Equal("probe", File.ReadAllText(output));
            }
            else
            {
                Assert.Throws<InvalidDataException>(() =>
                    FFprobe.ExtractExecutable(archive, output)
                );
                Assert.Equal("original", File.ReadAllText(output));
            }
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
