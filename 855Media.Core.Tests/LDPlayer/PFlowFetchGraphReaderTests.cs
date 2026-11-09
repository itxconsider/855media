using System.Net;
using System.Reflection;
using System.Text;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchGraphReaderTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("wrong-name")]
    [InlineData("permission")]
    [InlineData("invalid-id")]
    [InlineData("missing-token")]
    public async Task ReadsOnlyMatchedActorAndKeepsPageIdSeparate(string change)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Missing = change == "missing-token";
        using var scope = new PFlowNativeFetchScope(
            ld,
            3,
            "installation",
            allowGraphSessionRead: true
        );
        using var handler = new Handler(change);
        using var http = new HttpClient(handler);
        var result = await new PFlowFetchGraphReader().ReadAsync(
            ld,
            3,
            "installation",
            "SAM Outdoor",
            default,
            http
        );
        Assert.Equal(change == "missing-token" ? 0 : 1, handler.Calls);
        if (change != "valid")
        {
            Assert.Null(result);
            return;
        }
        Assert.Equal("61573646592140", result!.Id);
        Assert.Equal("541526682385407", Assert.Single(result.Pages).Id);
    }

    [Fact]
    public void OptionalScopeAllowsOnlyExactSessionReadAndDoesNotLeakToOtherDevices()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        using (new PFlowNativeFetchScope(ld, 3, "installation"))
            Assert.Throws<InvalidOperationException>(() =>
                PFlowNativeFetchScope.RequireAllowed(
                    ld,
                    3,
                    "installation",
                    PFlowFetchGraphReader.SessionCommand
                )
            );
        using (new PFlowNativeFetchScope(ld, 3, "installation", true))
        {
            PFlowNativeFetchScope.RequireAllowed(
                ld,
                3,
                "installation",
                PFlowFetchGraphReader.SessionCommand
            );
            Assert.Throws<InvalidOperationException>(() =>
                PFlowNativeFetchScope.RequireAllowed(ld, 3, "installation", "shell su 0 id")
            );
            using (new PFlowNativeFetchScope(ld, 4, "installation"))
                Assert.Throws<InvalidOperationException>(() =>
                    PFlowNativeFetchScope.RequireAllowed(
                        ld,
                        4,
                        "installation",
                        PFlowFetchGraphReader.SessionCommand
                    )
                );
        }
    }

    public class Device : DispatchProxy
    {
        public bool Missing;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("ExecuteAdbAsync", method!.Name);
            var command = (string)args![1]!;
            Assert.Equal(PFlowFetchGraphReader.SessionCommand, command);
            PFlowNativeFetchScope.RequireAllowed(
                (ILDPlayerService)(object)this,
                (int)args[0]!,
                (string?)args[2],
                command
            );
            return Task.FromResult(
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(
                        Missing ? "no token" : "\0EAA11111111111111111111111111111\0"
                    )
                )
            );
        }
    }

    private class Handler(string change) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("graph.facebook.com", request.RequestUri!.Host);
            Assert.DoesNotContain("EAA", request.RequestUri.ToString());
            var name = change == "wrong-name" ? "Harch Jing" : "SAM Outdoor";
            var id = change == "invalid-id" ? "not-an-id" : "61573646592140";
            return Task.FromResult(
                new HttpResponseMessage(
                    change == "permission" ? HttpStatusCode.Forbidden : HttpStatusCode.OK
                )
                {
                    Content = new StringContent(
                        System.Text.Json.JsonSerializer.Serialize(
                            new
                            {
                                id,
                                name,
                                accounts = new
                                {
                                    data = new[]
                                    {
                                        new { id = "541526682385407", name = "SAM Outdoor" },
                                    },
                                },
                            }
                        )
                    ),
                }
            );
        }
    }
}
