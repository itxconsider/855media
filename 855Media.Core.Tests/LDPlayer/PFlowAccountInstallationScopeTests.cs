using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowAccountInstallationScopeTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "855-account-scope-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    private string Installation(string version)
    {
        var path = Path.Combine(root, version);
        Directory.CreateDirectory(Path.Combine(path, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(path, "vms", "leidian45"));
        File.WriteAllText(
            Path.Combine(path, "vms", "config", "leidian45.config"),
            "{\"propertySettings.phoneAndroidId\":\"same-cloned-id\"}"
        );
        File.WriteAllText(Path.Combine(path, "vms", "leidian45", "data.vmdk"), "fixture");
        return path;
    }

    [Fact]
    public void SameIndexAndNameInDifferentInstallationsDoNotMatch()
    {
        var nine = Installation("LDPlayer9");
        var fourteen = Installation("LDPlayer14");
        var instances = new[]
        {
            new LDInstanceInfo { Index = 45, Name = "LD-45" },
        };
        var nineIdentity = PFlowDeviceRouting.ReadIdentity(nine, 45);
        var fourteenIdentity = PFlowDeviceRouting.ReadIdentity(fourteen, 45);
        var scope = PFlowAccountInstallationScope.ReadIdentities(fourteen, instances);
        Assert.Contains(fourteenIdentity, scope);
        Assert.DoesNotContain(nineIdentity, scope);
        Assert.Equal(scope, PFlowAccountInstallationScope.ReadIdentities(fourteen, instances));
        Assert.Equal(nineIdentity, PFlowDeviceRouting.ReadIdentity(nine, 45));
    }

    [Fact]
    public void MissingAndUnreadableInstancesNeverAuthorizeCachedAccounts()
    {
        var path = Installation("LDPlayer14");
        var instance = new LDInstanceInfo { Index = 45 };
        File.WriteAllText(Path.Combine(path, "vms", "config", "leidian45.config"), "broken");
        Assert.Empty(
            PFlowAccountInstallationScope.ReadIdentities(
                path,
                new[]
                {
                    instance,
                    new LDInstanceInfo { Index = 46 },
                }
            )
        );
    }

    [Fact]
    public void InstanceRenameStaysVisibleForMappingReview()
    {
        var path = Installation("LDPlayer14");
        var identity = PFlowDeviceRouting.ReadIdentity(path, 45);
        Assert.Contains(
            identity,
            PFlowAccountInstallationScope.ReadIdentities(
                path,
                new[]
                {
                    new LDInstanceInfo { Index = 45, Name = "Renamed" },
                }
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountStore.RequireCurrentBinding(
                new(identity, 45, "Old name", "com.facebook.katana", "123"),
                new[]
                {
                    new LDInstanceInfo { Index = 45, Name = "Renamed" },
                },
                path
            )
        );
    }
}
