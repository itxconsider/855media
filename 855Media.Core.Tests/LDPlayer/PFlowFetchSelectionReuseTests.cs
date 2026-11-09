using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchSelectionReuseTests
{
    private const string Page = "Bushcraft improvisation";

    [Fact]
    public async Task CompletedListVerificationUsesTheAlreadyOpenSelectedSheet()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeFetchDeviceTests.SwitchDevice>();
        var fake = (PFlowNativeFetchDeviceTests.SwitchDevice)(object)ld;
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.NotEmpty(await adapter.ScanAsync(default));
        var opens = fake.SheetOpens;
        var closes = fake.Backs;
        Assert.True(await adapter.SelectAsync(new(Page, "Page"), default));
        Assert.Equal(1, fake.SheetOpens - opens);
        Assert.Equal(1, fake.Backs - closes);
        Assert.Equal(0, fake.Forbidden);
    }

    [Fact]
    public async Task ActualAdapterHandsSelectedPageToDetailsWithoutAnotherSwitcher()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeFetchDeviceTests.SwitchDevice>();
        var fake = (PFlowNativeFetchDeviceTests.SwitchDevice)(object)ld;
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.True(await adapter.SelectAsync(new(Page, "Page"), default));
        fake.OwnCapture = FacebookLayoutRegistryTests
            .Fixture("ld14-tap-profile")
            .Replace("TAP Plumbing And Heating", Page);
        var opens = fake.SheetOpens;
        var details = await adapter.PageDetailsAsync(
            new FacebookPageInfo
            {
                PageName = Page,
                PageId = "12345",
                PersonaUid = "12345",
            },
            "98765",
            "Owner",
            default
        );
        Assert.NotNull(details);
        Assert.True(fake.DetailsHandoff);
        Assert.Equal(opens, fake.SheetOpens);
        Assert.Equal(0, fake.Forbidden);
        Assert.Null(
            await PFlowFetchSelectionHandoff.TryConsumeAsync(
                ld,
                901,
                "fixture",
                Page,
                "12345",
                "98765",
                default
            )
        );
    }

    [Theory]
    [InlineData("wrong-name")]
    [InlineData("input")]
    [InlineData("foreign")]
    [InlineData("reset")]
    public async Task ChangedActorOrContextInvalidatesAdapterReuse(string change)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeFetchDeviceTests.SwitchDevice>();
        var fake = (PFlowNativeFetchDeviceTests.SwitchDevice)(object)ld;
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.True(await adapter.SelectAsync(new(Page, "Page"), default));
        fake.OwnCapture = FacebookLayoutRegistryTests
            .Fixture("ld14-tap-profile")
            .Replace("TAP Plumbing And Heating", Page);
        var good = fake.OwnCapture;
        if (change == "wrong-name")
            fake.OwnCapture = good.Replace(Page, "Other Page");
        if (change == "foreign")
            fake.OwnCapture = good.Replace("com.facebook.katana", "other.app");
        if (change == "input")
            fake.OwnCapture = good.Replace(
                "</hierarchy>",
                "<node class='android.widget.EditText' package='com.facebook.katana'/></hierarchy>"
            );
        if (change == "reset")
            adapter.Configure([], null);
        var method = typeof(PFlowNativeFetchDevice).GetMethod(
            "VerifySelectedProfileAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        Task<bool> Verify() =>
            (Task<bool>)
                method.Invoke(
                    adapter,
                    [new PFlowFetchActor(Page, "Page"), CancellationToken.None]
                )!;
        if (change is "foreign" or "input")
            await Assert.ThrowsAsync<InvalidOperationException>(() => Verify());
        else
            Assert.False(await Verify());
        fake.OwnCapture = good;
        Assert.False(await Verify()); // A later good frame cannot resurrect rejected proof.
        Assert.Equal(0, fake.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualAdapterReusesOnlySameProcessOwnProfile(bool changedProcess)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeFetchDeviceTests.SwitchDevice>();
        var fake = (PFlowNativeFetchDeviceTests.SwitchDevice)(object)ld;
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.True(await adapter.SelectAsync(new(Page, "Page"), default));
        var opens = fake.SheetOpens;
        fake.OwnCapture = FacebookLayoutRegistryTests
            .Fixture("ld14-tap-profile")
            .Replace("TAP Plumbing And Heating", Page);
        if (changedProcess)
        {
            fake.Pid = "456";
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adapter.SelectAsync(new(Page, "Page"), default)
            );
        }
        else
            Assert.True(await adapter.SelectAsync(new(Page, "Page"), default));
        Assert.Equal(opens, fake.SheetOpens);
        Assert.Equal(0, fake.Forbidden);
    }

    [Fact]
    public async Task ActualAdapterNormalizesPidOrderDuringReuse()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNativeFetchDeviceTests.SwitchDevice>();
        var fake = (PFlowNativeFetchDeviceTests.SwitchDevice)(object)ld;
        fake.Pid = "123 456";
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.True(await adapter.SelectAsync(new(Page, "Page"), default));
        fake.Pid = "456\n123";
        fake.OwnCapture = FacebookLayoutRegistryTests
            .Fixture("ld14-tap-profile")
            .Replace("TAP Plumbing And Heating", Page);
        var opens = fake.SheetOpens;
        Assert.True(await adapter.SelectAsync(new(Page, "Page"), default));
        Assert.Equal(opens, fake.SheetOpens);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("index")]
    [InlineData("path")]
    [InlineData("name")]
    [InlineData("page")]
    [InlineData("owner")]
    [InlineData("policy")]
    public async Task HandoffCannotCrossRoutingOrInvocationContext(string mismatch)
    {
        using var atx = new AtxAgentClient();
        var ld = new LDPlayerService(atx);
        var other = new LDPlayerService(atx);
        using var policy =
            mismatch == "policy" ? null : new PFlowNativeFetchScope(ld, 901, "fixture");
        var calls = 0;
        using var handoff = new PFlowFetchSelectionHandoff(
            ld,
            901,
            "fixture",
            Page,
            "12345",
            "98765",
            _ =>
            {
                calls++;
                return Task.FromResult(true);
            }
        );
        Assert.Null(
            await PFlowFetchSelectionHandoff.TryConsumeAsync(
                mismatch == "service" ? other : ld,
                mismatch == "index" ? 902 : 901,
                mismatch == "path" ? "other" : "fixture",
                mismatch == "name" ? "Other" : Page,
                mismatch == "page" ? "55555" : "12345",
                mismatch == "owner" ? "11111" : "98765",
                default
            )
        );
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualPageReaderConsumesFreshHandoffOnceWithoutSwitchNavigation(bool verified)
    {
        using var atx = new AtxAgentClient();
        var ld = new LDPlayerService(atx);
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var calls = 0;
        using var handoff = new PFlowFetchSelectionHandoff(
            ld,
            901,
            "fixture",
            Page,
            "12345",
            "98765",
            _ =>
            {
                calls++;
                return Task.FromResult(verified);
            }
        );
        var flow = new FacebookPageFetchStateMachine("98765", Page);
        var method = typeof(LDPlayerService).GetMethod(
            "SwitchToPageFromProfileSwitcherAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var task =
            (Task<bool>)
                method.Invoke(
                    ld,
                    [
                        901,
                        "98765",
                        "Owner",
                        Page,
                        "12345",
                        "fixture",
                        CancellationToken.None,
                        null,
                        true,
                        flow,
                    ]
                )!;
        Assert.Equal(verified, await task);
        Assert.Equal(1, calls);
        Assert.Null(
            await PFlowFetchSelectionHandoff.TryConsumeAsync(
                ld,
                901,
                "fixture",
                Page,
                "12345",
                "98765",
                default
            )
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancelledHandoffCannotRunVerifier()
    {
        using var atx = new AtxAgentClient();
        var ld = new LDPlayerService(atx);
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var calls = 0;
        using var handoff = new PFlowFetchSelectionHandoff(
            ld,
            901,
            "fixture",
            Page,
            "12345",
            "98765",
            _ =>
            {
                calls++;
                return Task.FromResult(true);
            }
        );
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowFetchSelectionHandoff.TryConsumeAsync(
                ld,
                901,
                "fixture",
                Page,
                "12345",
                "98765",
                stop.Token
            )
        );
        Assert.Equal(0, calls);
    }
}
