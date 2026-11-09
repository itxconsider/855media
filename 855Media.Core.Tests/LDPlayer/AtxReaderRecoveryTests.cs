using System.Net;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class AtxReaderRecoveryTests
{
    private const string Xml =
        "<hierarchy><node package='com.facebook.katana' text='Profile'/></hierarchy>";

    [Fact]
    public void ReaderRecoveryInvalidatesOldActorProofButFreshVerificationCanCreateNewProof()
    {
        var binding = new PFlowAccountBinding(
            Guid.NewGuid().ToString(),
            73,
            "LD-73",
            "com.facebook.katana",
            "123"
        );
        var target = new FacebookPageTarget
        {
            AccountBinding = binding,
            UserUid = "123",
            PageId = "456",
            PageName = "Fixture Page",
            Type = "Page",
        };
        PFlowNativeIdentityScope? Fresh() =>
            PFlowNativeIdentityScope.FromSelectedName(
                binding,
                "1234",
                target,
                new(target.PageName, target.Type),
                [target]
            );
        var proof = Fresh()!;
        Assert.True(proof.Matches(binding, "1234", target));
        PFlowUiReaderEpoch.Invalidate(binding.InstanceIdentity);
        Assert.False(proof.Matches(binding, "1234", target));
        Assert.True(Fresh()!.Matches(binding, "1234", target));
    }

    private static int _port = 61000;

    private static (AtxAgentClient Client, Handler Handler) Setup(string mode = "ok")
    {
        var handler = new Handler { Mode = mode };
        var http = new HttpClient(handler);
        return (new AtxAgentClient(Interlocked.Increment(ref _port), http), handler);
    }

    [Fact]
    public async Task RestartOnlyAutomationAndRequireTwoFreshFrames()
    {
        var (client, handler) = Setup();
        using (client)
        {
            var checks = 0;
            Assert.Equal(
                Xml,
                await client.RecoverHierarchyAsync(
                    0,
                    _ =>
                    {
                        checks++;
                        return Task.FromResult(true);
                    },
                    default
                )
            );
            Assert.Equal(
                new[]
                {
                    "DELETE /uiautomator",
                    "POST /uiautomator",
                    "GET /dump/hierarchy",
                    "GET /dump/hierarchy",
                },
                handler.Calls
            );
            Assert.Equal(6, checks);
            Assert.Equal(2, handler.Reads);
        }
    }

    [Theory]
    [InlineData("http500")]
    [InlineData("empty")]
    [InlineData("malformed")]
    [InlineData("foreign")]
    [InlineData("stop-failed")]
    [InlineData("start-failed")]
    public async Task PersistentFailuresCannotGrantProofOrRestartRepeatedly(string mode)
    {
        var (client, handler) = Setup(mode);
        using (client)
        {
            Assert.Null(await client.RecoverHierarchyAsync(0, _ => Task.FromResult(true), default));
            var calls = handler.Calls.Count;
            Assert.Null(await client.RecoverHierarchyAsync(0, _ => Task.FromResult(true), default));
            Assert.Equal(calls, handler.Calls.Count);
            Assert.Single(handler.Calls, c => c == "DELETE /uiautomator");
        }
    }

    [Fact]
    public async Task ChangedProcessOrForegroundStopsBeforeServiceStartOrProof()
    {
        var (client, handler) = Setup();
        using (client)
        {
            var checks = 0;
            Assert.Null(
                await client.RecoverHierarchyAsync(0, _ => Task.FromResult(++checks < 2), default)
            );
            Assert.Equal(new[] { "DELETE /uiautomator" }, handler.Calls);
        }
        var (other, untouched) = Setup();
        using (other)
        {
            Assert.Null(await other.RecoverHierarchyAsync(0, _ => Task.FromResult(false), default));
            Assert.Empty(untouched.Calls);
        }
    }

    [Fact]
    public async Task TransientFrameResetsConsecutiveReadCount()
    {
        var (client, handler) = Setup("transient");
        using (client)
        {
            Assert.Equal(
                Xml,
                await client.RecoverHierarchyAsync(0, _ => Task.FromResult(true), default)
            );
            Assert.Equal(4, handler.Reads);
        }
    }

    [Fact]
    public async Task CancellationIsPropagatedAndDoesNotRestart()
    {
        var (client, handler) = Setup();
        using (client)
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.RecoverHierarchyAsync(0, _ => Task.FromResult(true), cancel.Token)
            );
            Assert.Empty(handler.Calls);
        }
    }

    [Fact]
    public async Task ParallelNormalReadsAreSerializedWithoutRecoveryOrCrossDeviceBlocking()
    {
        var (client, handler) = Setup();
        handler.Slow = true;
        using (client)
        {
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.DumpHierarchyAsync(0)));
            Assert.Equal(1, handler.Peak);
            Assert.Equal(20, handler.Reads);
            Assert.All(handler.Calls, c => Assert.Equal("GET /dump/hierarchy", c));
        }
    }

    public class Handler : HttpMessageHandler
    {
        public string Mode = "ok";
        public bool Slow;
        public int Reads,
            Active,
            Peak;
        public List<string> Calls = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var active = Interlocked.Increment(ref Active);
            Peak = Math.Max(Peak, active);
            try
            {
                Calls.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
                if (Slow)
                    await Task.Delay(5, ct);
                if (request.Method == HttpMethod.Delete)
                    return Response(Mode == "stop-failed" ? 500 : 200, "");
                if (request.Method == HttpMethod.Post)
                    return Response(Mode == "start-failed" ? 500 : 200, "");
                Reads++;
                return Mode switch
                {
                    "http500" => Response(500, "signal: killed"),
                    "empty" => Response(200, "<hierarchy/>"),
                    "malformed" => Response(200, "<hierarchy><node"),
                    "foreign" => Response(200, Xml.Replace("com.facebook.katana", "other.app")),
                    "transient" when Reads == 2 => Response(200, "<hierarchy/>"),
                    _ => Response(200, Xml),
                };
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }

        private static HttpResponseMessage Response(int status, string body) =>
            new((HttpStatusCode)status) { Content = new StringContent(body) };
    }
}
