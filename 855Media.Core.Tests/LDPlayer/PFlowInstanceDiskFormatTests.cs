using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowInstanceDiskFormatTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "855-disk-format-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private string Prepare(string extension)
    {
        Directory.CreateDirectory(Path.Combine(root, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(root, "vms", "leidian45"));
        File.WriteAllText(
            Path.Combine(root, "vms", "config", "leidian45.config"),
            "{\"propertySettings.phoneAndroidId\":\"fixture-id\"}"
        );
        var disk = Path.Combine(root, "vms", "leidian45", "data." + extension);
        File.WriteAllText(disk, "fixture");
        return disk;
    }

    [Theory]
    [InlineData("vmdk")]
    [InlineData("vdi")]
    public void SupportedDiskUsesExistingIdentityContract(string format)
    {
        var disk = Prepare(format);
        var legacyInput = JsonSerializer.Serialize(
            new[]
            {
                Path.GetFullPath(root).ToUpperInvariant(),
                "fixture-id",
                File.GetCreationTimeUtc(disk)
                    .Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }
        );
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyInput)));
        Assert.Equal(expected, PFlowDeviceRouting.ReadIdentity(root, 45));
        var instances = new[]
        {
            new LDInstanceInfo { Index = 45, Name = "Alucard" },
        };
        Assert.Contains(expected, PFlowAccountInstallationScope.ReadIdentities(root, instances));
        PFlowAccountStore.RequireCurrentBinding(
            new(expected, 45, "Alucard", "com.facebook.katana", "123"),
            instances,
            root
        );
    }

    [Fact]
    public void MissingDiskStillRejectsIdentity()
    {
        File.Delete(Prepare("vdi"));
        Assert.Throws<InvalidOperationException>(() => PFlowDeviceRouting.ReadIdentity(root, 45));
    }

    [Fact]
    public void TwoDataDiskFormatsFailWithoutGuessing()
    {
        Prepare("vmdk");
        Prepare("vdi");
        Assert.Throws<InvalidOperationException>(() => PFlowDeviceRouting.ReadIdentity(root, 45));
        Assert.Empty(
            PFlowAccountInstallationScope.ReadIdentities(
                root,
                new[] { new LDInstanceInfo { Index = 45 } }
            )
        );
    }

    [Fact]
    public void ChangedVdiCreationTimeInvalidatesPreviousBinding()
    {
        var disk = Prepare("vdi");
        var identity = PFlowDeviceRouting.ReadIdentity(root, 45);
        File.SetCreationTimeUtc(disk, File.GetCreationTimeUtc(disk).AddHours(-1));
        Assert.NotEqual(identity, PFlowDeviceRouting.ReadIdentity(root, 45));
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountStore.RequireCurrentBinding(
                new(identity, 45, "Alucard", "com.facebook.katana", "123"),
                new[]
                {
                    new LDInstanceInfo { Index = 45, Name = "Alucard" },
                },
                root
            )
        );
    }
}
