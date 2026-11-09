using System.Net;
using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowRootRemovalTests
{
    [Theory]
    [InlineData("shell su 0 id")]
    [InlineData("shell /system/xbin/su -c id")]
    [InlineData("shell sh -c 'su -c id'")]
    [InlineData("shell run-as com.facebook.katana id")]
    [InlineData("shell cat /data/data/com.facebook.katana/x")]
    [InlineData("shell cat /data/user/0/com.facebook.katana/x")]
    [InlineData("shell cat /data/user_de/0/com.facebook.katana/x")]
    [InlineData("shell rm -rf /data/system/x")]
    [InlineData("root")]
    [InlineData("remount")]
    [InlineData("disable-verity")]
    public async Task EveryTransportRejectsPrivilegedAccessWithoutFetchScope(string command)
    {
        using var handler = new DenyHttp();
        using var http = new HttpClient(handler);
        using var atx = new AtxAgentClient(httpClient: http);
        var ld = new LDPlayerService(atx);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ld.ExecuteAdbAsync(999, command, "missing")
        );
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            atx.ExecuteShellWithOutputAsync(999, command)
        );
        Assert.Equal(0, handler.Reads);
    }

    [Theory]
    [InlineData("shell pidof com.facebook.katana")]
    [InlineData("shell input tap 10 20")]
    [InlineData("shell dumpsys connectivity")]
    [InlineData("shell ime list -s")]
    [InlineData("shell /data/local/tmp/atx-agent server -d")]
    [InlineData("forward tcp:7912 tcp:7912")]
    [InlineData("shell rm /sdcard/DCIM/855Media-owned-job/photo.jpg")]
    public void NativeUiAndOwnedSharedMediaCommandsRemainAllowed(string command) =>
        PFlowDeviceAccessPolicy.RequireUnprivileged(command);

    [Fact]
    public async Task AtxFileEndpointsCannotReadOrWritePrivateStorage()
    {
        using var handler = new DenyHttp();
        using var http = new HttpClient(handler);
        using var atx = new AtxAgentClient(httpClient: http);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            atx.DownloadFileBytesAsync(999, "/data/data/com.facebook.katana/authentication")
        );
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            atx.UploadFileAsync(999, "missing", "/data/user/0/com.facebook.katana/x")
        );
        Assert.Equal(0, handler.Reads);
    }

    [Fact]
    public async Task RetiredPrivateExtractionAndCacheApisNeverTouchDeviceOrHttp()
    {
        using var handler = new DenyHttp();
        using var http = new HttpClient(handler);
        using var atx = new AtxAgentClient(httpClient: http);
        var ld = new LDPlayerService(atx);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ld.ExtractFacebookAccountsFromRootShellAsync(999)
        );
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ld.ExtractFacebookAccountsDirectFromRootAdbAsync(
                999,
                "com.facebook.katana",
                null,
                default
            )
        );
        await Assert.ThrowsAsync<NotSupportedException>(() => ld.ClearFacebookCacheAsync(999));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            atx.ClearAppCacheAsync(999, "com.facebook.katana")
        );
        var fake = DispatchProxy.Create<ILDPlayerService, DenyDevice>();
        var discovery = new PFlowAccountDiscoveryService(fake);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            discovery.ExtractFacebookAccountsFromRootShellAsync(999)
        );
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            discovery.ExtractFacebookAccountsDirectFromRootAdbAsync(999)
        );
        Assert.Throws<NotSupportedException>(() =>
            PagePostDeletionService.ForDevice(fake, 999, "missing", "1234567890")
        );
        Assert.False(
            await PFlowLiveUidVerification.VerifyPagePersonaAsync(
                fake,
                999,
                null,
                "1234567890",
                "2345678901",
                default
            )
        );
        Assert.Equal(0, ((DenyDevice)(object)fake).Reads + handler.Reads);
    }

    [Fact]
    public async Task RetiredReadersHonorCancellationBeforeAnyDeviceRead()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, DenyDevice>();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowGraphIdentityReader().TryReadAsync(ld, 1, null, ct.Token)
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowAccountDiscoveryService(ld).ExtractFacebookAccountsFromRootShellAsync(
                1,
                cancellationToken: ct.Token
            )
        );
        Assert.Equal(0, ((DenyDevice)(object)ld).Reads);
    }

    public class DenyDevice : DispatchProxy
    {
        public int Reads;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Reads++;
            throw new Exception("Unexpected device access: " + method!.Name);
        }
    }

    internal sealed class DenyHttp : HttpMessageHandler
    {
        internal int Reads;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Reads++;
            throw new Exception("Unexpected HTTP request");
        }
    }
}
