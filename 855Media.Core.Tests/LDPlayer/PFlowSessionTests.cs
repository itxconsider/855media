using System;
using System.Reflection;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSessionTests
{
    private const string Empty =
        "ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)\nmRecentsUid=1000\nmRecentsComponent=ComponentInfo{launcher/Recents}\n";
    private const string Occupied =
        Empty + " Recent tasks:\n  * Recent #0: TaskRecord{a #12 A=com.facebook.katana U=0}\n";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("success")]
    [InlineData("ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)")]
    public void UnsupportedOutputCannotProveEmpty(string? value) =>
        Assert.Null(PFlowSession.RecentAppsEmpty(value));

    [Fact]
    public void ValidUnfilteredDumpDistinguishesEmptyAndRemaining()
    {
        Assert.True(PFlowSession.RecentAppsEmpty(Empty));
        Assert.False(PFlowSession.RecentAppsEmpty(Occupied));
        Assert.Null(PFlowSession.RecentAppsEmpty(Empty + " Recent tasks:\n"));
        Assert.Null(PFlowSession.RecentAppsEmpty(Empty + "Permission Denial: dumpsys"));
        Assert.Null(PFlowSession.RecentAppsEmpty(Empty + " (nothing)"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("ambiguous")]
    [InlineData("tap-failed")]
    [InlineData("still-present")]
    [InlineData("unsupported")]
    public async Task FailedClearingIsReportedAndReturnsHome(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Mode = mode;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowSession.ClearAppsAsync(ld, 1, null, default)
        );
        Assert.Equal(2, fake.Home);
        Assert.Equal(mode is "tap-failed" or "still-present" ? 1 : 0, fake.Taps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyObservedEmptyStateSucceeds(bool initiallyEmpty)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.EmptyState = initiallyEmpty;
        await PFlowSession.ClearAppsAsync(ld, 1, null, default);
        Assert.Equal(initiallyEmpty ? 0 : 1, fake.Taps);
        Assert.Equal(2, fake.Home);
    }

    [Theory]
    [InlineData(12, false)]
    [InlineData(15, false)]
    [InlineData(12, true)]
    public async Task ObservedAndroid9ButtonClearsAndWaitsForTaskVerification(
        int index,
        bool delayed
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Layout = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                $"ld{index}-android9-recents.xml"
            )
        );
        fake.DelayLayout = delayed;
        await PFlowSession.ClearAppsAsync(ld, index, null, default);
        Assert.Equal(1, fake.Taps);
        Assert.Equal((617, 112), fake.LastTap);
        Assert.True(fake.EmptyState);
        Assert.Equal(2, fake.Home);
    }

    [Fact]
    public async Task HiddenAndroid9ClearAllIsRevealedThenTaskEmptinessIsVerified()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Layout = System.IO.File.ReadAllText(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld12-android9-recents.xml"
            )
        );
        fake.RevealAfterSwipes = 1;
        await PFlowSession.ClearAppsAsync(ld, 12, null, default);
        Assert.Equal(1, fake.Swipes);
        Assert.Equal(1, fake.Taps);
        Assert.True(fake.EmptyState);
        Assert.Equal(2, fake.Home);
    }

    [Fact]
    public void ExplicitSystemTaskTypesAreNotAppCardsAndNamesCannotSubstituteForTypes()
    {
        var home =
            Empty
            + " Recent tasks:\n * Recent #0: TaskRecord{a #12 A=launcher U=0}\n autoRemoveRecents=false isPersistable=false numFullscreen=1 activityType=2\n";
        Assert.True(PFlowSession.RecentAppsEmpty(home));
        Assert.True(PFlowSession.RecentAppsEmpty(home.Replace("activityType=2", "activityType=3")));
        Assert.False(
            PFlowSession.RecentAppsEmpty(home.Replace("activityType=2", "activityType=1"))
        );
        Assert.False(PFlowSession.RecentAppsEmpty(home.Replace(" activityType=2", "")));
        Assert.False(
            PFlowSession.RecentAppsEmpty(
                home + " * Recent #1: TaskRecord{b #15 A=com.facebook.katana}\n"
            )
        );
    }

    public class Device : DispatchProxy
    {
        public string Mode = "success";
        public bool EmptyState;
        public string? Layout;
        public bool DelayLayout;
        public int RevealAfterSwipes,
            Swipes;
        public (int X, int Y) LastTap;
        private int dumps;
        public int Home,
            Taps;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "StopFacebookAppAsync":
                    return Task.FromResult(true);
                case "SendKeyAsync":
                    if ((int)args![1]! == 3)
                        Home++;
                    return Task.FromResult(true);
                case "ExecuteAdbAsync":
                    return Task.FromResult(
                        Mode == "unsupported" ? "error: unavailable"
                        : EmptyState ? Empty
                        : Occupied
                    );
                case "DumpUiHierarchyAsync":
                    if (Layout != null && Swipes < RevealAfterSwipes)
                    {
                        var doc = System.Xml.Linq.XDocument.Parse(Layout);
                        doc.Descendants("node")
                            .Where(n =>
                                ((string?)n.Attribute("text"))?.Equals(
                                    "Clear all",
                                    StringComparison.OrdinalIgnoreCase
                                ) == true
                                || ((string?)n.Attribute("content-desc"))?.Equals(
                                    "Clear all",
                                    StringComparison.OrdinalIgnoreCase
                                ) == true
                            )
                            .ToList()
                            .ForEach(n => n.Remove());
                        return Task.FromResult<string?>(doc.ToString());
                    }
                    if (Layout != null)
                        return Task.FromResult<string?>(
                            DelayLayout && dumps++ == 0 ? "<hierarchy/>" : Layout
                        );
                    var button =
                        "<node package='com.android.systemui' clickable='true' bounds='[0,0][100,40]'><node package='com.android.systemui' text='Clear all' bounds='[0,0][100,40]'/></node>";
                    return Task.FromResult<string?>(
                        "<hierarchy>"
                            + (
                                Mode == "missing" ? ""
                                : Mode == "disabled"
                                    ? button.Replace(
                                        "clickable='true'",
                                        "clickable='true' enabled='false'"
                                    )
                                : Mode == "ambiguous"
                                    ? button + button.Replace("[0,0][100,40]", "[0,50][100,90]")
                                : button
                            )
                            + "</hierarchy>"
                    );
                case "TapAsync":
                    Taps++;
                    LastTap = ((int)args![1]!, (int)args[2]!);
                    EmptyState = Mode != "still-present";
                    return Task.FromResult(Mode != "tap-failed");
                case "SwipeAsync":
                    Assert.True((int)args![2]! < (int)args[4]!);
                    Swipes++;
                    return Task.FromResult(true);
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
