using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowRecentAppsNavigationTests
{
    [Fact]
    public async Task CapturedLd14QuickstepUsesHorizontalRevealAndExactClearControl()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecentsDevice>();
        var device = (RecentsDevice)(object)ld;
        device.Needed = 1;
        device.Horizontal = true;
        device.HiddenXml = FacebookLayoutRegistryTests.Fixture("ld14-recents-hidden");
        device.VisibleXml = FacebookLayoutRegistryTests.Fixture("ld14-recents-revealed");
        Assert.Null(LDPlayerService.FindRecentAppsClearTarget(device.HiddenXml));
        var button = await PFlowRecentAppsNavigation.FindClearAsync(
            ld,
            45,
            "ld14",
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal((162, 639), FacebookLayoutRegistry.RecentAppsClearPoint(button));
        Assert.Equal(1, device.Swipes);
        Assert.Equal(0, device.Taps);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("missing-container")]
    [InlineData("disabled")]
    [InlineData("ambiguous")]
    public void Android14ClearRequiresOwnedVisibleUniqueControl(string mutation)
    {
        var xml = System.Xml.Linq.XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("ld14-recents-revealed")
        );
        var button = xml.Descendants("node")
            .Single(n => (string?)n.Attribute("resource-id") == "com.ldmnq.launcher3:id/clear_all");
        if (mutation == "foreign")
            button.SetAttributeValue("package", "com.facebook.katana");
        if (mutation == "disabled")
            button.SetAttributeValue("enabled", "false");
        if (mutation == "missing-container")
            xml.Descendants("node")
                .Single(n =>
                    (string?)n.Attribute("resource-id") == "com.ldmnq.launcher3:id/overview_panel"
                )
                .SetAttributeValue("resource-id", "other");
        if (mutation == "ambiguous")
        {
            var duplicate = new System.Xml.Linq.XElement(button);
            duplicate.SetAttributeValue("bounds", "[66,700][258,796]");
            button.AddAfterSelf(duplicate);
        }
        Assert.Null(LDPlayerService.FindRecentAppsClearTarget(xml.ToString()));
    }

    [Theory]
    [InlineData("2", true)]
    [InlineData("3", true)]
    [InlineData("1", false)]
    [InlineData("", false)]
    public void Android14TaskMetadataRetainsExplicitTypeRequirement(string type, bool empty)
    {
        var text =
            "ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)\nmRecentsUid=10049\nmRecentsComponent=ComponentInfo{com.ldmnq.launcher3/.Recents}\n  Recent tasks:\n  * Recent #0: Task{fixture}\n    autoRemoveRecents=false isPersistable=true activityType="
            + type;
        Assert.Equal(empty, PFlowSession.RecentAppsEmpty(text));
    }

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

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(99)]
    public async Task Ld14TraversesMoreThanThreeCardsWithinBoundedBudget(int needed)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecentsDevice>();
        var device = (RecentsDevice)(object)ld;
        device.Needed = needed;
        device.Horizontal = true;
        device.HiddenXml = FacebookLayoutRegistryTests.Fixture("ld14-recents-hidden");
        device.VisibleXml = FacebookLayoutRegistryTests.Fixture("ld14-recents-revealed");
        var result = await PFlowRecentAppsNavigation.FindClearAsync(
            ld,
            900942,
            "fixture",
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal(needed <= 12, result != null);
        Assert.Equal(Math.Min(needed, 12), device.Swipes);
        Assert.Equal(0, device.Taps);
    }

    public class RecentsDevice : DispatchProxy
    {
        public int Needed,
            Swipes,
            Taps;
        public bool Horizontal;
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
                    if (Horizontal)
                    {
                        Assert.True((int)args![1]! < (int)args[3]!);
                        Assert.Equal((int)args[2]!, (int)args[4]!);
                    }
                    else
                    {
                        Assert.True((int)args![2]! < (int)args[4]!);
                        Assert.Equal((int)args[1]!, (int)args[3]!);
                    }
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
