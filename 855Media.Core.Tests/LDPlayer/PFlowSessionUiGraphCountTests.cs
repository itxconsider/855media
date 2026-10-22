using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowSessionUiGraphCountTests
{
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    public async Task TenConsecutiveReelsKeepActivationGraphAndNoSettings(
        bool reuseStartup,
        int expectedCalls
    )
    {
        const string owner = "100000000000001",
            page = "100000000000002",
            name = "Z Outdoors+";
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeIdentityScopeTests.Device>();
        var device = (PFlowNativeIdentityScopeTests.Device)(object)ld;
        var binding = new PFlowAccountBinding(
            "fixture-device",
            12,
            "LD-12",
            "com.facebook.katana",
            owner
        );
        var target = new FacebookPageTarget
        {
            UserUid = owner,
            PageId = page,
            PageName = name,
            Type = "Page",
        };
        using var handler = new GraphHandler { Id = owner, Name = "Fixture Owner" };
        using var client = new HttpClient(handler);
        var reader = new PFlowGraphIdentityReader(client);
        var startup = await reader.TryReadUidAsync(ld, 12, null, default);
        Assert.NotNull(startup);
        PFlowNativeIdentityScope? scope = null;
        var switches = 0;
        async Task<string> Activate(CancellationToken ct)
        {
            PFlowGraphIdentity? final = null;
            var method = await PFlowLiveUidVerification.ResolveAsync(
                target,
                async c =>
                {
                    if (reuseStartup && startup != null)
                    {
                        final = startup;
                        startup = null;
                        return final;
                    }
                    return final = await reader.TryReadUidAsync(ld, 12, null, c);
                },
                _ =>
                {
                    switches++;
                    handler.Id = page;
                    handler.Name = name;
                    return Task.FromResult(true);
                },
                _ => throw new InvalidOperationException("No settings-link fallback allowed"),
                (_, _, _) =>
                    throw new InvalidOperationException(
                        "No delegation read needed for canonical Page UID"
                    ),
                ct
            );
            scope = PFlowNativeIdentityScope.FromVerifiedGraph(binding, "123", target, final!);
            Assert.NotNull(scope);
            Assert.True(await scope.VerifyAsync(ld, null, ct));
            return method;
        }
        var session = new PFlowPostTargetSession();
        for (var i = 0; i < 10; i++)
        {
            var job = new PFlowJob
            {
                JobId = "fixture-" + i,
                BatchId = "same-batch",
                DeviceIndex = 12,
                AppPackage = binding.AppPackage,
                AccountBinding = binding,
                OwnerUid = owner,
                TargetId = page,
                TargetName = name,
                PostType = "Reel",
            };
            if (scope != null)
                Assert.True(await scope.VerifyAsync(ld, null, default));
            Assert.True(
                await session.VerifyAsync(
                    job,
                    Activate,
                    _ => Task.FromResult(true),
                    c => ld.DumpUiHierarchyAsync(12, null, c)!,
                    default,
                    device.ProfileXml()
                )
            );
            job.NativeIdentityScope = scope;
            Assert.True(scope!.Matches(job));
            // Production publication identity uses this same scope before/after content matching.
            Assert.True(await scope.VerifyAsync(ld, null, default));
            Assert.True(await scope.VerifyAsync(ld, null, default));
            job.State = PFlowState.Confirmed;
            job.ConfirmedUtc = DateTime.UtcNow;
            job.Evidence = "fixture-publication-confirmed";
        }
        Assert.Equal(expectedCalls, handler.Calls);
        Assert.Equal(1, switches);
        Assert.Equal(0, device.Taps);
        Assert.Equal(0, device.Switches);
        Assert.Equal(0, device.Restarts);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task ReuseOwnerBeforeEverySwitchButReadEveryDestinationFromGraph(int pages)
    {
        const string owner = "100000000000001";
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeIdentityScopeTests.Device>();
        var device = (PFlowNativeIdentityScopeTests.Device)(object)ld;
        var binding = new PFlowAccountBinding(
            "fixture-device",
            12,
            "LD-12",
            "com.facebook.katana",
            owner
        );
        using var handler = new GraphHandler { Id = owner, Name = "Fixture Owner" };
        using var client = new HttpClient(handler);
        var reader = new PFlowGraphIdentityReader(client);
        var startup = await reader.TryReadUidAsync(ld, 12, null, default);
        var proof = PFlowGraphActivationProof.Create(binding, "123", startup!)!;
        for (var p = 0; p < pages; p++)
        {
            var target = new FacebookPageTarget
            {
                PageId = (100000000000010L + p).ToString(),
                UserUid = owner,
                PageName = "Fixture Page " + p,
                Type = "Page",
            };
            var firstRead = true;
            var freshReads = 0;
            PFlowGraphIdentity? final = null;
            var method = await PFlowLiveUidVerification.ResolveAsync(
                target,
                async ct =>
                {
                    if (firstRead)
                    {
                        firstRead = false;
                        var reused = await proof.TryReuseAsync(binding, "123", ld, null, ct);
                        Assert.NotNull(reused);
                        return final = reused;
                    }
                    freshReads++;
                    return final = await reader.TryReadUidAsync(ld, 12, null, ct);
                },
                _ =>
                {
                    handler.Id = target.PageId;
                    handler.Name = target.PageName;
                    device.AuthToken += "nextActor"; // A controlled switch can change credentials.
                    device.UiOverride = device.ProfileXml().Replace("Z Outdoors+", target.PageName);
                    return Task.FromResult(true);
                },
                _ => throw new Exception("No settings fallback"),
                (_, _, _) => throw new Exception("No delegation needed"),
                default
            );
            Assert.Equal("Graph UID", method);
            Assert.Equal(1, freshReads);
            Assert.Equal(p + 2, handler.Calls);
            var scope = PFlowNativeIdentityScope.FromVerifiedGraph(binding, "123", target, final!)!;
            for (var reel = 0; reel < 2; reel++)
            {
                Assert.True(await scope.VerifyAsync(ld, null, default));
                Assert.True(await scope.VerifyAsync(ld, null, default));
                Assert.True(await scope.VerifyAsync(ld, null, default));
            }
            proof = PFlowGraphActivationProof.Create(binding, "123", final!)!;
        }
        Assert.Equal(1 + pages, handler.Calls);
        Assert.Equal(0, device.Taps);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("owner")]
    [InlineData("process")]
    [InlineData("binding")]
    [InlineData("foreground")]
    public async Task OwnerProofCannotSurviveUnexpectedChanges(string change)
    {
        const string owner = "100000000000001";
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeIdentityScopeTests.Device>();
        var device = (PFlowNativeIdentityScopeTests.Device)(object)ld;
        var binding = new PFlowAccountBinding(
            "fixture-device",
            12,
            "LD-12",
            "com.facebook.katana",
            owner
        );
        using var handler = new GraphHandler { Id = owner, Name = "Fixture Owner" };
        using var client = new HttpClient(handler);
        var graph = await new PFlowGraphIdentityReader(client).TryReadUidAsync(
            ld,
            12,
            null,
            default
        );
        var proof = PFlowGraphActivationProof.Create(binding, "123", graph!)!;
        var token = device.AuthToken;
        if (change == "token")
            device.AuthToken += "unexpected";
        if (change == "owner")
            device.OwnerUid = "100000000000099";
        if (change == "process")
            device.Pid = "999";
        if (change == "foreground")
            device.Activity = "other.app/Activity";
        var requested = change == "binding" ? binding with { DeviceIndex = 99 } : binding;
        Assert.Null(await proof.TryReuseAsync(requested, "123", ld, null, default));
        device.AuthToken = token;
        device.OwnerUid = owner;
        device.Pid = "123";
        device.Activity = "com.facebook.katana/Activity";
        Assert.Null(await proof.TryReuseAsync(binding, "123", ld, null, default));
    }

    [Fact]
    public async Task CachedOwnerDoesNotAuthorizeWrongDestinationGraphUid()
    {
        const string owner = "100000000000001";
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeIdentityScopeTests.Device>();
        var binding = new PFlowAccountBinding(
            "fixture-device",
            12,
            "LD-12",
            "com.facebook.katana",
            owner
        );
        using var handler = new GraphHandler { Id = owner, Name = "Fixture Owner" };
        using var client = new HttpClient(handler);
        var reader = new PFlowGraphIdentityReader(client);
        var startup = await reader.TryReadUidAsync(ld, 12, null, default);
        var proof = PFlowGraphActivationProof.Create(binding, "123", startup!)!;
        var first = true;
        var target = new FacebookPageTarget
        {
            PageId = "100000000000010",
            UserUid = owner,
            PageName = "Expected Page",
            Type = "Page",
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowLiveUidVerification.ResolveAsync(
                target,
                async ct =>
                {
                    if (first)
                    {
                        first = false;
                        return await proof.TryReuseAsync(binding, "123", ld, null, ct);
                    }
                    return await reader.TryReadUidAsync(ld, 12, null, ct);
                },
                _ => Task.FromResult(true),
                _ => Task.FromResult(false),
                (_, _, _) => Task.FromResult(false),
                default
            )
        );
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(true, 2, 1)]
    [InlineData(false, 3, 2)]
    public async Task ActivePageFirstSavesReturnSwitchAndGraphRead(
        bool preferActive,
        int requests,
        int expectedSwitches
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeIdentityScopeTests.Device>();
        var device = (PFlowNativeIdentityScopeTests.Device)(object)ld;
        var binding = new PFlowAccountBinding(
            "fixture-device",
            12,
            "LD-12",
            "com.facebook.katana",
            "100000000000001"
        );
        var a = new FacebookPageTarget
        {
            PageId = "100000000000010",
            PageName = "Z Outdoors+",
            UserUid = binding.OwnerUid,
            AccountBinding = binding,
            Type = "Page",
        };
        var b = new FacebookPageTarget
        {
            PageId = "100000000000011",
            PageName = "Fixture Page B",
            UserUid = binding.OwnerUid,
            AccountBinding = binding,
            Type = "Page",
        };
        using var handler = new GraphHandler { Id = a.PageId, Name = a.PageName };
        using var client = new HttpClient(handler);
        var reader = new PFlowGraphIdentityReader(client);
        var startup = await reader.TryReadUidAsync(ld, 12, null, default);
        var proof = PFlowGraphActivationProof.Create(binding, "123", startup!)!;
        var active = await proof.TryReuseAsync(binding, "123", ld, null, default);
        var targets = preferActive
            ? PFlowTargetOrder.PreferActive(new[] { b, a }, p => p, binding, active)
            : new[] { b, a };
        Assert.Equal(1, handler.Calls); // Ordering itself never calls Graph.
        var switches = 0;
        foreach (var target in targets)
        {
            var first = true;
            PFlowGraphIdentity? final = null;
            Assert.Equal(
                "Graph UID",
                await PFlowLiveUidVerification.ResolveAsync(
                    target,
                    async ct =>
                    {
                        if (first)
                        {
                            first = false;
                            return final = await proof.TryReuseAsync(binding, "123", ld, null, ct);
                        }
                        return final = await reader.TryReadUidAsync(ld, 12, null, ct);
                    },
                    _ =>
                    {
                        switches++;
                        handler.Id = target.PageId;
                        handler.Name = target.PageName;
                        device.AuthToken += "nextActor";
                        device.UiOverride = device
                            .ProfileXml()
                            .Replace("Z Outdoors+", target.PageName);
                        return Task.FromResult(true);
                    },
                    _ => throw new Exception("No settings fallback"),
                    (_, _, _) => throw new Exception("No delegation needed"),
                    default
                )
            );
            var scope = PFlowNativeIdentityScope.FromVerifiedGraph(binding, "123", target, final!)!;
            Assert.True(await scope.VerifyAsync(ld, null, default));
            proof = PFlowGraphActivationProof.Create(binding, "123", final!)!;
        }
        Assert.Equal(requests, handler.Calls);
        Assert.Equal(expectedSwitches, switches);
        Assert.Equal(0, device.Taps);
    }

    private sealed class GraphHandler : HttpMessageHandler
    {
        public string Id = "",
            Name = "";
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Assert.Equal("graph.facebook.com", request.RequestUri!.Host);
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { id = Id, name = Name }),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
