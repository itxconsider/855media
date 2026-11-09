using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowOptionalApiAccessTests
{
    [Fact]
    public async Task ActualAdbEntryAndDirectTransportHonorExactOptInOnly()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "855-api-policy-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);
        try
        {
            using var atx = new AtxAgentClient();
            var ld = new LDPlayerService(atx);
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                ld.ExecuteAdbAsync(42, PFlowFetchGraphReader.SessionCommand, folder)
            );
            using (new PFlowNativeFetchScope(ld, 42, folder, true))
            {
                // Missing adb.exe is a deliberate transport boundary: no process,
                // device, token or network call. Both real policy checks must pass.
                var output = await ld.ExecuteAdbAsync(
                    42,
                    PFlowFetchGraphReader.SessionCommand,
                    folder
                );
                Assert.Contains("ADB was not found", output);
                await Assert.ThrowsAsync<NotSupportedException>(() =>
                    ld.ExecuteAdbAsync(43, PFlowFetchGraphReader.SessionCommand, folder)
                );
                await Assert.ThrowsAsync<NotSupportedException>(() =>
                    ld.ExecuteAdbAsync(42, PFlowFetchGraphReader.SessionCommand, folder + "-other")
                );
                using (new PFlowNativeFetchScope(ld, 42, folder))
                    await Assert.ThrowsAsync<NotSupportedException>(() =>
                        ld.ExecuteAdbAsync(42, PFlowFetchGraphReader.SessionCommand, folder)
                    );
            }
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                ld.ExecuteAdbAsync(42, PFlowFetchGraphReader.SessionCommand, folder)
            );
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData("shell su 0 id")]
    [InlineData("root")]
    [InlineData("shell cat /data/data/com.facebook.katana/authentication")]
    [InlineData("shell run-as com.facebook.katana id")]
    [InlineData(
        "shell su 0 base64 /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/authentication; id"
    )]
    [InlineData(
        "shell su 0 base64 /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/authentication "
    )]
    public void OptInCannotAuthorizeOtherPrivilegedCommandsOrGlobalAtxAccess(string command)
    {
        using var atx = new AtxAgentClient();
        var ld = new LDPlayerService(atx);
        using var scope = new PFlowNativeFetchScope(ld, 42, "fixture", true);
        Assert.Throws<NotSupportedException>(() =>
            PFlowDeviceAccessPolicy.RequireAdbCommand(ld, 42, "fixture", command)
        );
        Assert.Throws<NotSupportedException>(() =>
            PFlowDeviceAccessPolicy.RequireUnprivileged(PFlowFetchGraphReader.SessionCommand)
        );
    }

    [Fact]
    public void OptInIsBoundToTheServiceInstanceAndDisposedScopeCannotGrantAccess()
    {
        using var atx = new AtxAgentClient();
        var ld = new LDPlayerService(atx);
        var other = new LDPlayerService(atx);
        using (new PFlowNativeFetchScope(ld, 42, "fixture", true))
        {
            PFlowDeviceAccessPolicy.RequireAdbCommand(
                ld,
                42,
                "fixture",
                PFlowFetchGraphReader.SessionCommand
            );
            Assert.Throws<NotSupportedException>(() =>
                PFlowDeviceAccessPolicy.RequireAdbCommand(
                    other,
                    42,
                    "fixture",
                    PFlowFetchGraphReader.SessionCommand
                )
            );
        }
        Assert.Throws<NotSupportedException>(() =>
            PFlowDeviceAccessPolicy.RequireAdbCommand(
                ld,
                42,
                "fixture",
                PFlowFetchGraphReader.SessionCommand
            )
        );
    }
}
