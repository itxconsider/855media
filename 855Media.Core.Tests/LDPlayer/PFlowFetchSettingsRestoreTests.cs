using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchSettingsRestoreTests
{
    private static string Settings =>
        FacebookLayoutRegistryTests.Fixture(
            "ld71-fetch-restore-profile-settings",
            "PFlowScreenTime"
        );

    private static string Profile(string name) =>
        FacebookLayoutRegistryTests
            .Fixture("ld61-tail-talks-profile-title", "PFlowScreenTime")
            .Replace("Tail Talks+", name);

    [Theory]
    [InlineData("Profile Settings")]
    [InlineData("Page Settings")]
    public void CapturedSettingsRequiresBackInsteadOfItsAvatarDropdown(string header)
    {
        var xml = Settings.Replace("Profile Settings", header);
        Assert.Null(PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(xml));
        Assert.Equal(
            PFlowPersonaSwitcherService.NativeRouteKind.BackFromSettings,
            PFlowPersonaSwitcherService
                .PlanNativeRoute(AtxAgentClient.ParseBloksHierarchy(xml))
                .Kind
        );
    }

    [Theory]
    [InlineData("More profile settings")]
    [InlineData("Search")]
    [InlineData("Unrelated future action")]
    public void AnonymousWrapperCannotDisguiseADifferentActionAsTheSwitcher(string action)
    {
        var xml =
            $"<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node package='com.facebook.katana' text='Actor One' bounds='[100,100][400,150]'/><node package='com.facebook.katana' text='2 followers' bounds='[100,160][400,200]'/><node package='com.facebook.katana' clickable='true' enabled='true' bounds='[600,100][700,160]'><node package='com.facebook.katana' clickable='true' enabled='true' content-desc='{action}' bounds='[600,100][700,160]'/></node></node></hierarchy>";
        Assert.Null(PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(xml));
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("disabled-back")]
    [InlineData("duplicate-back")]
    [InlineData("missing-back")]
    public void UnsafeOrUnusableSettingsCannotAuthorizeBack(string mode)
    {
        var xml = Mutate(Settings, mode);
        Assert.Equal(
            PFlowPersonaSwitcherService.NativeRouteKind.Blocked,
            PFlowPersonaSwitcherService
                .PlanNativeRoute(AtxAgentClient.ParseBloksHierarchy(xml))
                .Kind
        );
    }

    private static string Mutate(string xml, string mode)
    {
        var doc = XDocument.Parse(xml);
        var back = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Back");
        if (mode == "foreign")
            return xml.Replace("com.facebook.katana", "other.app");
        if (mode == "input")
            back.SetAttributeValue("class", "android.widget.EditText");
        if (mode == "disabled-back")
            back.SetAttributeValue("enabled", "false");
        if (mode == "duplicate-back")
            back.Parent!.Add(new XElement(back));
        if (mode == "missing-back")
            back.Remove();
        return doc.ToString();
    }

    private static (ILDPlayerService Ld, Device Fake, PFlowPersonaSwitcherService Service) Harness()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        var clock = DateTime.UtcNow;
        return (
            ld,
            fake,
            new(
                ld,
                null,
                (delay, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    clock += delay;
                    return Task.CompletedTask;
                },
                () => clock
            )
        );
    }

    [Fact]
    public async Task CapturedSettingsExitsOnceThenOpensRealProfileSwitcher()
    {
        var (ld, fake, service) = Harness();
        using var scope = new PFlowNativeFetchScope(ld, 900971, "fixture");
        Assert.True(await service.OpenDiscoverySwitcherAsync(900971, "fixture", default));
        Assert.Equal(1, fake.Backs);
        Assert.Equal(1, fake.SwitcherTaps);
        Assert.Equal(0, fake.OtherTaps);
        Assert.Equal("sheet", fake.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrIneffectiveBackIsBoundedAndNeverTapsSettingsDropdown(bool stuck)
    {
        var (ld, fake, service) = Harness();
        fake.StuckBack = stuck;
        fake.BackOk = stuck;
        using var scope = new PFlowNativeFetchScope(ld, 900971, "fixture");
        Assert.False(await service.OpenDiscoverySwitcherAsync(900971, "fixture", default));
        Assert.Equal(stuck ? 2 : 1, fake.Backs);
        Assert.Equal(0, fake.SwitcherTaps + fake.OtherTaps);
    }

    [Fact]
    public async Task ActualTypeDiscoveryFinallyRestoresStartingActorFromCapturedSettings()
    {
        var (ld, fake, service) = Harness();
        using var scope = new PFlowNativeFetchScope(ld, 900971, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 900971, "fixture", _ => Task.CompletedTask);
        var rows = new[]
        {
            new PFlowPersonaSwitcherService.NativeSwitcherActor("Ultra Plumbing", "Page", true),
            new PFlowPersonaSwitcherService.NativeSwitcherActor("Laura Lindholm", "Profile", false),
        };
        var method = typeof(PFlowNativeFetchDevice).GetMethod(
            "DiscoverTypesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var actors = await (Task<PFlowFetchActor[]>)
            method.Invoke(adapter, [service, CancellationToken.None, rows])!;
        Assert.Equal(2, actors.Length);
        Assert.Equal("Ultra Plumbing", fake.Actor);
        Assert.Equal(1, fake.SelectionTaps);
        Assert.Equal(0, fake.OtherTaps);
    }

    public class Device : DispatchProxy
    {
        public string State = "settings",
            Actor = "Laura Lindholm";
        public bool BackOk = true,
            StuckBack;
        public int Backs,
            SwitcherTaps,
            SelectionTaps,
            OtherTaps;

        private string Xml()
        {
            if (State == "settings")
                return Settings;
            if (State == "profile")
                return Profile(Actor);
            var doc = XDocument.Parse(
                FacebookLayoutRegistryTests
                    .Fixture("ld64-switcher-static-footer-captured", "PFlowScreenTime")
                    .Replace("Personal Owner", "Laura Lindholm")
                    .Replace("Mattea Page+", "Ultra Plumbing")
            );
            foreach (var n in doc.Descendants("node"))
                n.SetAttributeValue("selected", "false");
            var selected = doc.Descendants("node")
                .Single(n => n.Attribute("content-desc")?.Value == Actor);
            foreach (var n in selected.DescendantsAndSelf())
                n.SetAttributeValue("selected", "true");
            return doc.ToString();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "ExecuteAdbAsync" when (string)args![1]! == "shell pidof com.facebook.katana":
                    return Task.FromResult("123");
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml())
                    );
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "PressBackAsync":
                    Backs++;
                    if (BackOk && !StuckBack)
                        State = "profile";
                    return Task.FromResult(BackOk);
                case "TapAsync":
                    var point = ((int)args![1]!, (int)args[2]!);
                    if (State == "profile")
                    {
                        var sw = PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(
                            Profile(Actor)
                        );
                        Assert.Equal(ScreenTimeHierarchy.Center(sw)!.Value, point);
                        SwitcherTaps++;
                        State = "sheet";
                    }
                    else if (State == "sheet")
                    {
                        SelectionTaps++;
                        Actor = point.Item2 >= 570 ? "Ultra Plumbing" : "Laura Lindholm";
                        State = "profile";
                    }
                    else
                    {
                        OtherTaps++;
                        throw new Exception("Must never tap a Settings control");
                    }
                    return Task.FromResult(true);
                case "TakeScreenshotAsync":
                    return Task.FromResult<byte[]?>(null);
                default:
                    throw new Exception(
                        "No restart/Graph/unplanned action allowed: " + method.Name
                    );
            }
        }
    }
}
