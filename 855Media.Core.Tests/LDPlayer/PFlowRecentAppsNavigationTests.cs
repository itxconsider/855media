using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowRecentAppsNavigationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(99)]
    public async Task HiddenClearAllIsReobservedAfterBoundedScrollsTowardTop(int needed)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecentsDevice>();
        var device = (RecentsDevice)(object)ld;
        device.Needed = needed;
        var button = await PFlowRecentAppsNavigation.FindClearAsync(
            ld,
            969,
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal(needed <= 3, button != null);
        Assert.Equal(Math.Min(needed, 3), device.Swipes);
        Assert.Equal(0, device.Taps);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("disabled")]
    [InlineData("ambiguous")]
    public async Task UnsupportedOrUnusableControlsDoNotCauseScrolling(string state)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecentsDevice>();
        var device = (RecentsDevice)(object)ld;
        device.State = state;
        Assert.Null(
            await PFlowRecentAppsNavigation.FindClearAsync(
                ld,
                969,
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(0, device.Swipes + device.Taps);
    }

    [Fact]
    public async Task RetainedOffscreenClearLabelDoesNotPreventRevealingIt()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecentsDevice>();
        var device = (RecentsDevice)(object)ld;
        device.State = "offscreen";
        device.Needed = 1;
        Assert.NotNull(
            await PFlowRecentAppsNavigation.FindClearAsync(
                ld,
                969,
                null,
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(1, device.Swipes);
    }

    [Fact]
    public async Task CapturedLd64StackRevealsTheExactClearButtonAfterScrollingToTop()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecentsDevice>();
        var device = (RecentsDevice)(object)ld;
        device.Needed = 1;
        device.HiddenXml = FacebookLayoutRegistryTests.Fixture("ld64-recents-clear-hidden");
        device.VisibleXml = FacebookLayoutRegistryTests.Fixture("ld64-recents-clear-revealed");
        Assert.Null(LDPlayerService.FindRecentAppsClearTarget(device.HiddenXml));
        var button = await PFlowRecentAppsNavigation.FindClearAsync(
            ld,
            64,
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal((617, 112), FacebookLayoutRegistry.RecentAppsClearPoint(button));
        Assert.Equal(1, device.Swipes);
    }

    public class RecentsDevice : DispatchProxy
    {
        public int Needed,
            Swipes,
            Taps;
        public string State = "normal";
        public string? HiddenXml,
            VisibleXml;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    if (HiddenXml != null)
                        return Task.FromResult(Swipes < Needed ? HiddenXml : VisibleXml);
                    var clear =
                        "<node package='com.android.systemui' text='CLEAR ALL' clickable='true' enabled='true' bounds='[250,30][318,65]'/>";
                    var body =
                        State == "disabled" ? clear.Replace("enabled='true'", "enabled='false'")
                        : State == "ambiguous"
                            ? clear + clear.Replace("[250,30][318,65]", "[160,30][240,65]")
                        : Swipes >= Needed ? clear
                        : State == "offscreen"
                            ? clear.Replace("[250,30][318,65]", "[250,700][318,735]")
                        : "";
                    return Task.FromResult<string?>(
                        "<hierarchy><node package='com.android.systemui' resource-id='com.android.systemui:id/recents_view' bounds='[0,0][325,585]'>"
                            + body
                            + "</node></hierarchy>"
                            is var xml
                        && State == "foreign"
                            ? xml.Replace("com.android.systemui", "com.facebook.katana")
                            : xml
                    );
                case "SwipeAsync":
                    Assert.True((int)args![2]! < (int)args[4]!);
                    Assert.Equal((int)args[1]!, (int)args[3]!);
                    Swipes++;
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    throw new Exception("Lookup must not tap");
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
