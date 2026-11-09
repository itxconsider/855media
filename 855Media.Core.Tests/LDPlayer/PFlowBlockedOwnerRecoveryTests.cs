using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowBlockedOwnerRecoveryTests
{
    private const string Owner = "Rudolph White";
    private const string Page = "Danny Stenhouse";
    private static string Menu => FacebookLayoutRegistryTests.Fixture("ld33-disabled-owner-menu");

    [Fact]
    public void CapturedMenuDoesNotAuthorizeDisabledShortcutTap()
    {
        Assert.True(PFlowNativeAccountNavigator.MenuSurface(Menu));
        var raw = PFlowPostingNameVerification.PersonalShortcut(Menu, Owner, requireUsable: false);
        Assert.NotNull(raw);
        Assert.Null(PFlowPostingNameVerification.PersonalShortcut(Menu, Owner));
        Assert.Null(FacebookLayoutRegistry.UsableCenter(raw));
        Assert.True(PFlowNativeAccountNavigator.OwnerRecoveryFrame(Menu, "com.facebook.katana"));
    }

    [Fact]
    public void ExpectedSwitcherIsAnAllowedRecoveryFrame()
    {
        var sheet = new Device().Sheet();
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(sheet));
        Assert.False(
            PFlowPostingNameVerification.SafeNameResponseContext(sheet, "com.facebook.katana")
        );
        Assert.True(PFlowNativeAccountNavigator.OwnerRecoveryFrame(sheet, "com.facebook.katana"));
        Assert.False(
            PFlowNativeAccountNavigator.OwnerRecoveryFrame(
                sheet.Replace("com.facebook.katana", "other.app"),
                "com.facebook.katana"
            )
        );
        var input = XDocument.Parse(sheet);
        input.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("class", "android.widget.EditText")
            )
        );
        Assert.False(
            PFlowNativeAccountNavigator.OwnerRecoveryFrame(input.ToString(), "com.facebook.katana")
        );
        Assert.True(
            PFlowNativeAccountNavigator.OwnerRecoveryFrame("<hierarchy/>", "com.facebook.katana")
        );
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("wrong-owner", false)]
    [InlineData("duplicate", false)]
    [InlineData("wrong-destination", false)]
    [InlineData("owner-page-badge", false)]
    [InlineData("disabled-target", false)]
    public async Task SharedRecoveryRequiresExactPersonalOwnerAndFreshSelection(
        string mode,
        bool expected
    )
    {
        var (service, device, _) = Harness(mode);
        var guards = 0;
        Assert.Equal(
            expected,
            await service.RecoverPersonalOwnerAsync(
                900933,
                Owner,
                "fixture",
                default,
                _ =>
                {
                    guards++;
                    return Task.CompletedTask;
                }
            )
        );
        Assert.True(guards > 1);
        Assert.Equal(
            mode is "wrong-owner" or "duplicate" or "disabled-target" ? 0 : 1,
            device.OwnerTaps
        );
        Assert.DoesNotContain(device.Points, p => p == (441, 156)); // Captured disabled shortcut.
        if (expected)
        {
            Assert.True(device.OwnerSelected);
            Assert.False(device.IsSheet);
        }
    }

    [Fact]
    public async Task ChangedCompleteMembershipIsRejectedAfterSelection()
    {
        var (service, device, _) = Harness("changed-membership");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecoverPersonalOwnerAsync(
                900933,
                Owner,
                "fixture",
                default,
                _ => Task.CompletedTask
            )
        );
        Assert.Contains("actor list", error.Message);
        Assert.Equal(1, device.OwnerTaps);
    }

    [Fact]
    public async Task FreshGuardAndCancellationPreventOwnerTap()
    {
        var (service, device, _) = Harness("ok");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecoverPersonalOwnerAsync(
                900933,
                Owner,
                "fixture",
                default,
                _ => throw new InvalidOperationException("Worker or process changed")
            )
        );
        Assert.Empty(device.Points);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RecoverPersonalOwnerAsync(
                900933,
                Owner,
                "fixture",
                cancel.Token,
                ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Empty(device.Points);
    }

    private static (
        PFlowPersonaSwitcherService Service,
        Device Device,
        ILDPlayerService Ld
    ) Harness(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Mode = mode;
        var clock = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (delay, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                clock += delay;
                return Task.CompletedTask;
            },
            () => clock
        );
        return (service, device, ld);
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("pid-change", false)]
    [InlineData("worker-change", false)]
    [InlineData("foreign-foreground", false)]
    [InlineData("input", false)]
    [InlineData("unknown-sibling", true)]
    [InlineData("missing-profile-row", true)]
    [InlineData("conflicting-owner-row", false)]
    [InlineData("feed-ok", true)]
    [InlineData("feed-stuck", false)]
    [InlineData("feed-pid-change", false)]
    [InlineData("feed-worker-change", false)]
    [InlineData("feed-foreign-foreground", false)]
    [InlineData("feed-wrong-owner", false)]
    [InlineData("feed-conflicting-owner-row", false)]
    [InlineData("feed-disabled-target", false)]
    [InlineData("feed-owner-page-badge", false)]
    [InlineData("feed-pending-after-reveal", false)]
    [InlineData("feed-pid-after-reveal", false)]
    public async Task ActualSavedStartupUsesSharedRecoveryAndKeepsWorkerProcessAndInputGuards(
        string mode,
        bool expected
    )
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "owner-recovery-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path.Combine(directory, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(directory, "vms", "leidian900933"));
        File.WriteAllText(
            Path.Combine(directory, "vms", "config", "leidian900933.config"),
            "{\"propertySettings.phoneAndroidId\":\"fixture-owner-recovery\"}"
        );
        File.WriteAllText(Path.Combine(directory, "vms", "leidian900933", "data.vmdk"), "fixture");
        try
        {
            var (_, device, ld) = Harness(mode);
            device.IsSheet = false;
            if (mode.StartsWith("feed-", StringComparison.Ordinal))
            {
                device.HiddenFeed = true;
                device.Mode = mode[5..];
            }
            var binding = new PFlowAccountBinding(
                PFlowDeviceRouting.ReadIdentity(directory, 900933),
                900933,
                "fixture-LD",
                "com.facebook.katana",
                "1234567890123"
            );
            FacebookPageTarget[] saved =
            [
                new()
                {
                    AccountBinding = binding,
                    UserUid = binding.OwnerUid,
                    PageId = binding.OwnerUid,
                    PersonaUid = binding.OwnerUid,
                    PageName = Owner,
                    Type = "Profile",
                },
                new()
                {
                    AccountBinding = binding,
                    UserUid = binding.OwnerUid,
                    PageId = "2345678901234",
                    PersonaUid = "2345678901234",
                    PageName = Page,
                    Type = "Page",
                },
                new()
                {
                    AccountBinding = binding,
                    UserUid = binding.OwnerUid,
                    PageId = "3456789012345",
                    PersonaUid = "3456789012345",
                    PageName = "Metsätalous",
                    Type = "Page",
                },
            ];
            if (mode == "missing-profile-row")
                saved = saved.Where(t => t.Type != "Profile").ToArray();
            if (mode is "conflicting-owner-row" or "feed-conflicting-owner-row")
                saved[0].Type = "Page";
            var nav = new PFlowNativeAccountNavigator(
                ld,
                directory,
                _ => Owner,
                delay: (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                graphIdentity: (_, _) => throw new Exception("No API allowed"),
                postingNameOnly: true,
                savedTargets: _ => saved
            );
            var account = new PFlowPersonalAccount { Binding = binding, Name = Owner };
            var automatic = new PFlowAutomaticSessionService(
                nav,
                new NoCredentials(),
                _ => Task.CompletedTask,
                postingNameOnly: true
            );
            if (expected)
            {
                await automatic.EnsureAsync(account, _ => Task.CompletedTask, default, true);
                Assert.Equal(PFlowAuthenticationState.Authenticated, account.Authentication);
                Assert.Equal(1, device.OwnerTaps);
                Assert.True(device.OwnerSelected);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    automatic.EnsureAsync(account, _ => Task.CompletedTask, default, true)
                );
                Assert.Equal(mode == "feed-owner-page-badge" ? 1 : 0, device.OwnerTaps);
            }
            Assert.DoesNotContain(device.Points, p => p == (441, 156));
            if (mode == "feed-ok")
            {
                Assert.Equal(3, device.Reveals);
                Assert.Equal(1, device.FeedLinks);
            }
            if (mode == "feed-stuck")
            {
                Assert.Equal(3, device.Reveals);
                Assert.Equal(1, device.FeedLinks);
            }
            if (mode is "feed-pending-after-reveal" or "feed-pid-after-reveal")
            {
                Assert.Equal(3, device.Reveals);
                Assert.Equal(0, device.FeedLinks);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class NoCredentials : IPFlowCredentialStore
    {
        public Task<string> SaveAsync(
            PFlowAccountBinding binding,
            PFlowCredential credential,
            CancellationToken ct = default
        ) => throw new Exception("No credential writes");

        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding binding,
            string reference,
            CancellationToken ct = default
        ) => throw new Exception("No credential reads");

        public void Delete(string reference) => throw new Exception("No credential deletion");
    }

    public class Device : DispatchProxy
    {
        public bool IsSheet = true,
            OwnerSelected;
        public int OwnerTaps;
        public bool HiddenFeed,
            HomeVisible;
        public int Reveals,
            FeedLinks;
        public string Mode = "ok";
        public List<(int X, int Y)> Points = [];

        public string Sheet()
        {
            var xml = FacebookLayoutRegistryTests
                .Fixture("ld55-switcher-selected-page-captured", "PFlowScreenTime")
                .Replace("Bushcraft improvisation", Page)
                .Replace("Personal Owner", Owner)
                .Replace("HDPlumbers+", "Metsätalous");
            if (Mode == "wrong-owner")
                xml = xml.Replace(Owner, "Other owner");
            var doc = XDocument.Parse(xml);
            XElement Row(string name) =>
                doc.Descendants("node")
                    .Single(n =>
                        n.Attribute("content-desc")?.Value == name
                        || n.Attribute("content-desc")
                            ?.Value.StartsWith(name + ",", StringComparison.Ordinal) == true
                    );
            var owner = Mode == "wrong-owner" ? null : Row(Owner);
            if (OwnerSelected)
            {
                foreach (var n in doc.Descendants("node"))
                    n.SetAttributeValue("selected", "false");
                owner!.SetAttributeValue("selected", "true");
                Row(Page)
                    .Add(
                        new XElement(
                            "node",
                            new XAttribute("package", "com.facebook.katana"),
                            new XAttribute("text", "Page")
                        )
                    );
            }
            if (Mode == "owner-page-badge" && OwnerSelected)
                owner!.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("text", "Page")
                    )
                );
            if (Mode == "disabled-target")
                owner!.SetAttributeValue("enabled", "false");
            if (Mode == "duplicate")
                Row(Page).Parent!.Add(new XElement(owner!));
            if (Mode == "changed-membership" && OwnerSelected)
                Row("Metsätalous").Remove();
            if (Mode is "missing-profile-row" or "conflicting-owner-row")
                Row("Metsätalous").Remove();
            if (Mode == "input")
                doc.Root!.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("class", "android.widget.EditText")
                    )
                );
            if (Mode == "unknown-sibling")
            {
                var sibling = Row("Metsätalous");
                sibling
                    .Descendants("node")
                    .Where(n => n.Attribute("text")?.Value == "Page")
                    .Remove();
                foreach (var n in sibling.DescendantsAndSelf())
                {
                    if (
                        n.Attribute("content-desc")?.Value is { } desc
                        && desc.StartsWith("Metsätalous", StringComparison.Ordinal)
                    )
                        n.SetAttributeValue(
                            "content-desc",
                            desc.Replace("Metsätalous", "New Page")
                        );
                    if (n.Attribute("text")?.Value == "Metsätalous")
                        n.SetAttributeValue("text", "New Page");
                }
            }
            return doc.ToString();
        }

        private string Current() =>
            HiddenFeed ? HiddenFrame()
            : HomeVisible ? FacebookLayoutRegistryTests.Fixture("ld42-fetch-recovery-feed")
            : IsSheet ? Sheet()
            : OwnerSelected ? Menu.Replace(Page, Owner)
            : Menu;

        private string HiddenFrame()
        {
            var xml = FacebookLayoutRegistryTests.Fixture("ld57-startup-owner-hidden-feed");
            if (Mode == "pending-after-reveal" && Reveals >= 3)
            {
                var doc = XDocument.Parse(xml);
                doc.Root!.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("text", "Uploading your reel")
                    )
                );
                return doc.ToString();
            }
            return xml;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "GetInstancesAsync":
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                        new()
                        {
                            Index = 900933,
                            Name = Mode == "worker-change" && IsSheet ? "wrong-LD" : "fixture-LD",
                            IsAndroidStarted = true,
                        },
                    ]);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(
                        Mode == "foreign-foreground" && IsSheet
                            ? "other.app/Main"
                            : "com.facebook.katana/Main"
                    );
                case "ExecuteAdbAsync":
                    Assert.Equal("shell pidof com.facebook.katana", args![1]);
                    return Task.FromResult(
                        Mode == "pid-change" && IsSheet
                        || Mode == "pid-after-reveal" && Reveals >= 3
                            ? "456"
                            : "123"
                    );
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Current())
                    );
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Current());
                case "PressBackAsync":
                    IsSheet = false;
                    return Task.FromResult(true);
                case "SwipeAsync":
                    if (HiddenFeed)
                        Reveals++;
                    return Task.FromResult(true);
                case "OpenDeepLinkAsync":
                    Assert.Equal("fb://feed", args![1]);
                    FeedLinks++;
                    if (Mode != "stuck")
                    {
                        HiddenFeed = false;
                        HomeVisible = true;
                    }
                    return Task.FromResult(true);
                case "TapAsync":
                    var point = ((int)args![1]!, (int)args[2]!);
                    Points.Add(point);
                    if (HomeVisible)
                    {
                        Assert.Equal((52, 92), point);
                        HomeVisible = false;
                    }
                    else if (IsSheet)
                    {
                        var owner = ScreenTimeHierarchy
                            .Nodes(Sheet())
                            .Single(n => n.Attribute("content-desc")?.Value == Owner);
                        Assert.Equal(ScreenTimeHierarchy.Center(owner)!.Value, point);
                        OwnerTaps++;
                        OwnerSelected = Mode != "wrong-destination";
                        IsSheet = false;
                    }
                    else
                    {
                        Assert.Equal(
                            PFlowPersonaSwitcherService.ParseNodeBoundsCenter(
                                PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(Menu)!
                            ),
                            point
                        );
                        IsSheet = true;
                    }
                    return Task.FromResult(true);
                default:
                    throw new InvalidOperationException("Unexpected device call: " + method.Name);
            }
        }
    }
}
