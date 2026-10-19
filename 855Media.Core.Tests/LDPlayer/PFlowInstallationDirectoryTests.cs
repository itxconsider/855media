using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowInstallationDirectoryTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "ld-installation-" + Guid.NewGuid().ToString("N")
    );
    private string Actual => Path.Combine(directory, "actual-LDPlayer9");

    public PFlowInstallationDirectoryTests()
    {
        Directory.CreateDirectory(Path.Combine(Actual, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(Actual, "vms", "leidian1"));
        File.WriteAllText(Path.Combine(Actual, "ldconsole.exe"), "fixture, never executed");
        File.WriteAllText(
            Path.Combine(Actual, "vms", "config", "leidian1.config"),
            "{\"propertySettings.phoneAndroidId\":\"test-new-pc\"}"
        );
        File.WriteAllText(Path.Combine(Actual, "vms", "leidian1", "data.vmdk"), "test disk");
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ILDPlayerService Service(string? console = null)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, ConsoleResolver>();
        ((ConsoleResolver)(object)ld).Console = console ?? Path.Combine(Actual, "ldconsole.exe");
        return ld;
    }

    [Fact]
    public void StaleConfiguredFolderUsesTheSameInstallationAsDetectedConsole()
    {
        var stale = Path.Combine(directory, "missing-old-installation");
        var resolved = PFlowDeviceRouting.ResolveInstallationDirectory(Service(), stale);
        Assert.Equal(Actual, resolved);
        var candidates = PFlowAccountStore.CollectLegacy(
            [
                new()
                {
                    DeviceName = "Steaves Small Engine",
                    Type = "Profile",
                    AppClone = "FB-01",
                    PageId = "123",
                    UserUid = "123",
                },
            ],
            [new() { Index = 1, Name = "Steaves Small Engine" }],
            resolved
        );
        Assert.Empty(candidates.Issues);
        Assert.Equal(
            PFlowDeviceRouting.ReadIdentity(Actual, 1),
            Assert.Single(candidates.Accounts).Binding.InstanceIdentity
        );
    }

    [Fact]
    public void ExistingTrailingSlashAndBindingHashRemainUnchanged()
    {
        var configured = Actual + Path.DirectorySeparatorChar;
        var identity = PFlowDeviceRouting.ReadIdentity(configured, 1);
        var resolved = PFlowDeviceRouting.ResolveInstallationDirectory(Service(), configured);
        Assert.Equal(configured, resolved);
        Assert.Equal(identity, PFlowDeviceRouting.ReadIdentity(resolved, 1));
    }

    [Fact]
    public void ConsoleFileSettingIsNormalizedToInstallationFolder()
    {
        var resolved = PFlowDeviceRouting.ResolveInstallationDirectory(
            Service(),
            Path.Combine(Actual, "ldconsole.exe")
        );
        Assert.Equal(Actual, resolved);
        Assert.NotEmpty(PFlowDeviceRouting.ReadIdentity(resolved, 1));
    }

    [Theory]
    [InlineData("missing.exe")]
    [InlineData("adb.exe")]
    public void MissingOrWrongConsoleDoesNotInventAnInstallation(string name)
    {
        var console = Path.Combine(directory, name);
        if (name == "adb.exe")
            File.WriteAllText(console, "not an LD console");
        Assert.Throws<InvalidOperationException>(() =>
            PFlowDeviceRouting.ResolveInstallationDirectory(Service(console), Actual)
        );
    }

    public class ConsoleResolver : DispatchProxy
    {
        public string Console = "";

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name == "ResolveLdConsolePath"
                ? Console
                : throw new NotSupportedException("Unexpected device operation: " + method.Name);
    }
}
