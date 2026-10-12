using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class LDNetworkInfoTests
{
    [Theory]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData("2606:4700:4700::1111", "2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8", "8.8.8.8")]
    [InlineData("172.16.1.97", null)]
    [InlineData("127.0.0.1", null)]
    [InlineData("10.0.2.15", null)]
    [InlineData("192.168.0.1", null)]
    [InlineData("234.145.0.21", null)]
    [InlineData("100.64.0.1", null)]
    [InlineData("fe80::1", null)]
    [InlineData("fc00::1", null)]
    [InlineData("8.8.8.8;whoami", null)]
    public void AcceptsPublicAddressesAndRejectsLocalOrNonUnicast(string input, string? expected) =>
        Assert.Equal(expected, LDNetworkInfoService.ParsePublicIp("{\"ip\":\"" + input + "\"}"));

    [Theory]
    [InlineData("curl: SSL certificate problem")]
    [InlineData("{\"ip\":null}")]
    [InlineData("{\"ip\":123}")]
    [InlineData("{}")]
    [InlineData("<html>rate limit</html>")]
    public void RejectsInvalidGuestOutput(string output) =>
        Assert.Null(LDNetworkInfoService.ParsePublicIp(output));

    [Fact]
    public void KeepsObservedIpOnLocationErrorAndRejectsMismatchedLocation()
    {
        var info = LDNetworkInfoService.ParseLocation(
            "8.8.8.8",
            "{\"success\":true,\"ip\":\"1.1.1.1\",\"city\":\"Wrong City\"}"
        );
        Assert.Equal("8.8.8.8", info.PublicIp);
        Assert.Equal("Unavailable", info.Location);
        Assert.Equal(
            "Unavailable",
            LDNetworkInfoService.ParseLocation("8.8.8.8", "{\"success\":false}").Location
        );
    }

    [Fact]
    public async Task ProbesEachGuestButCachesLocationForSharedIpAndRechecksChangedIp()
    {
        var fake = new GuestNetwork();
        var service = new LDNetworkInfoService(fake);
        var first = await service.ReadAsync(3, "ld-folder", default);
        var second = await service.ReadAsync(4, "ld-folder", default);
        Assert.Equal("New York", first.Location);
        Assert.Equal(first, second);
        Assert.Equal(2, fake.IpCalls);
        Assert.Equal(1, fake.LocationCalls);
        fake.Ip = "1.1.1.1";
        var changed = await service.ReadAsync(3, "ld-folder", default);
        Assert.Equal("1.1.1.1", changed.PublicIp);
        Assert.Equal(2, fake.LocationCalls);
        Assert.All(fake.Commands, c => Assert.Contains("shell curl --fail", c));
        Assert.All(fake.Indices, i => Assert.True(i is 3 or 4));
    }

    [Fact]
    public async Task CannotSubstituteHostIpWhenGuestFails()
    {
        var fake = new GuestNetwork { Ip = "invalid" };
        var result = await new LDNetworkInfoService(fake).ReadAsync(3, null, default);
        Assert.Equal("", result.PublicIp);
        Assert.Equal(0, fake.LocationCalls);
    }

    [Fact]
    public async Task LocationFailureRetainsPublicIpAndIsCached()
    {
        var fake = new GuestNetwork { FailLocation = true };
        var service = new LDNetworkInfoService(fake);
        var result = await service.ReadAsync(3, null, default);
        Assert.Equal("8.8.8.8", result.PublicIp);
        Assert.Equal("Unavailable", result.Location);
        await service.ReadAsync(4, null, default);
        Assert.Equal(1, fake.LocationCalls);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToCachedLocationFailure()
    {
        var fake = new GuestNetwork();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LDNetworkInfoService(fake).ReadAsync(3, null, canceled.Token)
        );
        Assert.Equal(0, fake.IpCalls);
    }

    private sealed class GuestNetwork : LDPlayerService
    {
        public string Ip = "8.8.8.8";
        public bool FailLocation;
        public int IpCalls,
            LocationCalls;
        public List<string> Commands = [];
        public List<int> Indices = [];

        public override Task<string> ExecuteAdbAsync(
            int index,
            string adbCommand,
            string? customPath = null,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(adbCommand);
            Indices.Add(index);
            if (adbCommand.Contains("ipify.org"))
            {
                IpCalls++;
                return Task.FromResult("{\"ip\":\"" + Ip + "\"}");
            }
            LocationCalls++;
            if (FailLocation)
                throw new InvalidOperationException("Synthetic outage");
            Assert.Contains("https://ipwho.is/" + Ip, adbCommand);
            return Task.FromResult(
                "{\"success\":true,\"ip\":\""
                    + Ip
                    + "\",\"city\":\"New York\",\"region\":\"New York\",\"country\":\"United States\"}"
            );
        }
    }
}
