using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSurfsharkOptionTests
{
    [Fact]
    public void SettingDefaultsOffAndPersistsWhenEnabled()
    {
        Assert.False(JsonSerializer.Deserialize<AutoPostConfig>("{}")!.CheckSurfsharkConnection);
        var config = new AutoPostConfig { CheckSurfsharkConnection = true };
        Assert.True(
            JsonSerializer
                .Deserialize<AutoPostConfig>(JsonSerializer.Serialize(config))!
                .CheckSurfsharkConnection
        );
    }

    [Fact]
    public async Task WaitsForSurfsharkRatherThanAcceptingPlainInternet()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.ReadyAfter = 75;
        await PFlowConnectionChecks.WaitForSurfsharkAsync(
            ld,
            4,
            null,
            CancellationToken.None,
            device.Delay,
            () => device.Now
        );
        Assert.InRange(device.Seconds, 75, 77);
    }

    [Theory]
    [InlineData(true, 10066)]
    [InlineData(false, 10065)]
    public async Task WrongOwnerOrUnvalidatedVpnDoesNotPass(bool validated, int owner)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Owner = owner;
        device.Validated = validated;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowConnectionChecks.WaitForSurfsharkAsync(
                ld,
                4,
                null,
                CancellationToken.None,
                device.Delay,
                () => device.Now
            )
        );
        Assert.Equal(180, device.Seconds);
    }

    [Fact]
    public async Task MissingPackageFailsWithoutWaiting()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Installed = false;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowConnectionChecks.WaitForSurfsharkAsync(
                ld,
                4,
                null,
                CancellationToken.None,
                device.Delay,
                () => device.Now
            )
        );
        Assert.Contains("not installed", error.Message);
        Assert.Equal(0, device.Seconds);
    }

    [Fact]
    public async Task CancellationStopsImmediately()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowConnectionChecks.WaitForSurfsharkAsync(
                ld,
                4,
                null,
                new CancellationToken(true),
                device.Delay,
                () => device.Now
            )
        );
        Assert.Equal(0, device.Commands);
    }

    public class Device : DispatchProxy
    {
        private readonly DateTime _start = new(2026, 10, 20);
        public DateTime Now = new(2026, 10, 20);
        public int ReadyAfter,
            Commands,
            Owner = 10065;
        public bool Installed = true,
            Validated = true;
        public double Seconds => (Now - _start).TotalSeconds;

        public Task Delay(TimeSpan duration, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Now += duration;
            return Task.CompletedTask;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("ExecuteAdbAsync", method!.Name);
            Commands++;
            var command = (string)args![1]!;
            if (command == "shell pm list packages -U com.surfshark.vpnclient.android")
                return Task.FromResult(
                    Installed ? "package:com.surfshark.vpnclient.android uid:10065\n" : ""
                );
            Assert.Equal("shell dumpsys connectivity", command);
            var vpn =
                Seconds >= ReadyAfter
                    ? $"  NetworkAgentInfo{{ network{{101}} ni{{[state: CONNECTED/CONNECTED]}} nc{{[Transports: VPN Capabilities: INTERNET{(Validated ? "&VALIDATED" : "")} EstablishingAppUid: {Owner}]}} }}\n"
                    : "";
            return Task.FromResult(
                "Active default network: 100\nCurrent Networks:\n  NetworkAgentInfo{ network{100} ni{[state: CONNECTED/CONNECTED]} nc{[Capabilities: INTERNET&VALIDATED]}}\n"
                    + vpn
            );
        }
    }
}
