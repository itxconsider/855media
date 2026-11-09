using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowNativeFetchDeviceTests
{
    [Fact]
    public async Task ScrolledPageSettingsWithoutLinkRowsClosesItsOwnNavigationStack()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var fake = (UiDevice)(object)ld;
        fake.Page = true;
        fake.SettingsCapture = FacebookLayoutRegistryTests.Fixture("ld14-scrolled-page-settings");
        Assert.True(PFlowNativeFetchDevice.HasProfileSettingsHeader(fake.SettingsCapture));
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.Null(await adapter.ReadNumericIdAsync(new("Fixture Owner", "Page"), default));
        Assert.False(fake.Settings);
        Assert.Equal(1, fake.BackCount);
        await adapter.PrepareSwitcherAfterLinkLookupAsync(default, (_, _) => Task.CompletedTask);
        Assert.Equal(1, fake.FeedRequests);
        Assert.Equal(0, fake.Forbidden);
    }

    [Fact]
    public async Task PersonalDetailsFeedReturnSatisfiesThePendingLinkNavigationObligation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var fake = (UiDevice)(object)ld;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.NotNull(await adapter.ReadNumericIdAsync(new("Fixture Owner", "Profile"), default));
        await adapter.ReturnToFeedAfterPersonalDetailsAsync(default, (_, _) => Task.CompletedTask);
        Assert.Equal(1, fake.FeedRequests);
        await adapter.PrepareSwitcherAfterLinkLookupAsync(default, (_, _) => Task.CompletedTask);
        Assert.Equal(1, fake.FeedRequests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NativeLinkLookupRequiresFreshFeedBeforeAnotherSwitcher(
        bool unavailable,
        bool cancelled
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.ProfileCopyControl = unavailable;
        d.ClipboardReady = false;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        // No link attempt means ordinary/API-backed selection needs no extra navigation.
        await adapter.PrepareSwitcherAfterLinkLookupAsync(default);
        Assert.Equal(0, d.FeedRequests);
        var id = await adapter.ReadNumericIdAsync(new("Fixture Owner", "Profile"), default);
        Assert.Equal(unavailable ? null : "123456789012345", id);
        Assert.False(d.Settings);
        using var cancel = new CancellationTokenSource();
        if (cancelled)
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                adapter.PrepareSwitcherAfterLinkLookupAsync(cancel.Token)
            );
            Assert.Equal(0, d.FeedRequests);
        }
        await adapter.PrepareSwitcherAfterLinkLookupAsync(default, (_, _) => Task.CompletedTask);
        Assert.Equal(1, d.FeedRequests);
        await adapter.PrepareSwitcherAfterLinkLookupAsync(default);
        Assert.Equal(1, d.FeedRequests);
        Assert.Equal(0, d.Forbidden);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task PersonalCopyProfileLinkResolvesOnlyAfterFreshCopy(
        bool shortLink,
        bool clipboardReady
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.ProfileCopyControl = true;
        d.ClipboardReady = clipboardReady;
        d.RevealAfter = 1;
        d.CopiedLink = shortLink ? "https://www.facebook.com/share/ExampleProfile/" : d.Link;
        var calls = 0;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask,
            (link, _) =>
            {
                calls++;
                Assert.True(d.ProfileCopied);
                Assert.Equal(d.CopiedLink, link);
                return Task.FromResult<string?>("123456789012345");
            }
        ).ReadNumericIdAsync(new("Fixture Owner", "Profile"), default);
        Assert.Equal(clipboardReady ? "123456789012345" : null, id);
        Assert.Equal(clipboardReady && shortLink ? 1 : 0, calls);
        Assert.Equal(clipboardReady, d.ProfileCopied);
        Assert.False(d.Settings);
        Assert.Equal(0, d.Forbidden);
    }

    [Fact]
    public void CapturedMichaelProfileAllowsBirthdayScroll()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld11-birthday-profile");
        var screen = AtxAgentClient.ParseBloksHierarchy(xml);
        Assert.True(PFlowProfileDates.MatchesName(screen, "Michael Petersen"));
        Assert.False(PFlowUiPrivacy.ContainsInput(xml));
        Assert.False(AtxAgentClient.IsMenuPageInBloks(screen));
        Assert.False(PFlowProfileDates.ReachedProfileFeed(screen, false));
    }

    [Theory]
    [InlineData("ld36-fetch-own-profile", "Harch Sive Jing", 772)]
    [InlineData("ld43-fetch-own-profile", "Steven Velasquez", 826)]
    public async Task CapturedLowerOwnSettingsButtonReadsNumericId(
        string fixture,
        string name,
        int y
    )
    {
        var xml = FacebookLayoutRegistryTests.Fixture(fixture);
        Assert.Equal((648, y), PFlowNativeFetchDevice.OwnSettingsControl(xml));
        var missingOptions = System.Xml.Linq.XDocument.Parse(xml);
        var options = missingOptions
            .Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "See options");
        options.SetAttributeValue("clickable", "false");
        Assert.Null(PFlowNativeFetchDevice.OwnSettingsControl(missingOptions.ToString()));
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.OwnCapture = xml;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        Assert.Equal(
            "123456789012345",
            await adapter.ReadNumericIdAsync(new(name, "Profile"), default)
        );
        Assert.Equal(1, d.SettingsTaps);
        Assert.Equal((648, y), d.LastTap);
        Assert.False(d.Settings);
        Assert.Equal(0, d.Forbidden);
        Assert.Null(
            PFlowNativeFetchDevice.OwnSettingsControl(
                xml.Replace("com.facebook.katana", "other.app")
            )
        );
    }

    [Theory]
    [InlineData("ld36-fetch-own-profile", "Harch Sive Jing", 772)]
    [InlineData("ld43-fetch-own-profile", "Steven Velasquez", 826)]
    public async Task CapturedLowerOwnSettingsButtonConfirmsAccountType(
        string fixture,
        string name,
        int y
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.OwnCapture = FacebookLayoutRegistryTests.Fixture(fixture);
        using var scope = new PFlowNativeFetchScope(ld, 902, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 902, "fixture", _ => Task.CompletedTask);
        var method = typeof(PFlowNativeFetchDevice).GetMethod(
            "ReadActorTypeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var task = (Task<string>)method.Invoke(adapter, [name, CancellationToken.None])!;

        Assert.Equal("Profile", await task);
        Assert.Equal((648, y), d.LastTap);
        Assert.Equal(1, d.SettingsTaps);
        Assert.False(d.Settings);
        Assert.Equal(1, d.BackCount);
        Assert.Equal(0, d.Forbidden);
    }

    [Fact]
    public async Task PageSettingsRecoveryUsesSavedDestinationWithoutPrivateAccountCache()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, RecoveryDevice>();
        var d = (RecoveryDevice)(object)ld;
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        var navigator = new PFlowNativeAccountNavigator(
            ld,
            "fixture",
            _ => "Fixture Owner",
            delay: (_, _) => Task.CompletedTask,
            postingNameOnly: true
        );
        var method = typeof(PFlowNativeAccountNavigator).GetMethod(
            "ReturnFromPageSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        var binding = new PFlowAccountBinding(
            "fixture",
            901,
            "LD-901",
            "com.facebook.katana",
            "123456789012345"
        );
        Assert.True(await (Task<bool>)method.Invoke(navigator, [binding, CancellationToken.None])!);
        Assert.Equal("Fixture Owner", d.Destination);
        Assert.Equal(0, d.PrivateReads);
    }

    [Fact]
    public async Task NativePolicyBlocksPrivateCacheClearingBeforeAtxFallback()
    {
        using var atx = new AtxAgentClient();
        var ld = new LDPlayerService(atx);
        using var policy = new PFlowNativeFetchScope(ld, 901, "missing-installation");
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ld.ClearFacebookCacheAsync(901, "missing-installation")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewPageWithoutIdRequiresNativePolicyAndFreshSelectedActor(
        bool wrongDestination
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, SwitchDevice>();
        var d = (SwitchDevice)(object)ld;
        d.WrongDestination = wrongDestination;
        var target = new FacebookPageTarget { Type = "Page", PageName = "Bushcraft improvisation" };
        Assert.False(
            await new PFlowPersonaSwitcherService(ld).SelectNativeTargetAsync(
                901,
                target,
                "fixture",
                default,
                postingNameOnly: true
            )
        );
        using var policy = new PFlowNativeFetchScope(ld, 901, "fixture");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var selected = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).SelectAsync(new(target.PageName, "Page"), timeout.Token);
        Assert.Equal(!wrongDestination, selected);
        Assert.True(d.SheetOpens > 0);
        Assert.Equal(0, d.Forbidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualAdapterReadsOnlyTheSelectedOwnSettingsNumericLink(bool page)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Page = page;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var adapter = new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask);
        var id = await adapter.ReadNumericIdAsync(
            new("Fixture Owner", page ? "Page" : "Profile"),
            default
        );
        Assert.Equal("123456789012345", id);
        Assert.Equal(1, d.SettingsTaps);
        Assert.False(d.Settings);
        Assert.Equal(0, d.Forbidden);
    }

    [Fact]
    public async Task UsernameLinkDoesNotBecomeNumericIdOrInvokeResolverOrRoot()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Link = "https://www.facebook.com/fixture.owner";
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).ReadNumericIdAsync(new("Fixture Owner", "Profile"), default);
        Assert.Null(id);
        Assert.Equal(0, d.Forbidden);
        Assert.False(d.Settings);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    [InlineData(true, 6)]
    public async Task OwnSettingsLinkBelowViewportIsScrolledAndSettingsClosed(
        bool page,
        int revealAfter
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Page = page;
        d.RevealAfter = revealAfter;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).ReadNumericIdAsync(new("Fixture Owner", page ? "Page" : "Profile"), default);
        Assert.Equal("123456789012345", id);
        Assert.Equal(revealAfter, d.Swipes);
        Assert.False(d.Settings);
        Assert.Equal(0, d.Forbidden);
    }

    [Fact]
    public async Task MissingLinkHasBoundedScrollAndStillLeavesSettings()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.RevealAfter = 99;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).ReadNumericIdAsync(new("Fixture Owner", "Profile"), default);
        Assert.Null(id);
        Assert.InRange(d.Swipes, 1, 6);
        Assert.False(d.Settings);
    }

    [Fact]
    public async Task PageLinkRowIsOpenedOnceAfterScrolling()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Page = true;
        d.PageLinkControl = true;
        d.RevealAfter = 1;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).ReadNumericIdAsync(new("Fixture Owner", "Page"), default);
        Assert.Equal("123456789012345", id);
        Assert.Equal(1, d.PageLinkTaps);
        Assert.False(d.Settings);
    }

    [Fact]
    public async Task SettingsScrollRejectsChangedProcessBeforeReadingLink()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.RevealAfter = 1;
        d.ChangePidAfterScroll = true;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeFetchDevice(
                ld,
                901,
                "fixture",
                _ => Task.CompletedTask
            ).ReadNumericIdAsync(new("Fixture Owner", "Profile"), default)
        );
        Assert.Contains("Facebook restarted during Fetch", error.Message);
        Assert.DoesNotContain("another app", error.Message);
        Assert.Equal(1, d.Swipes);
    }

    [Fact]
    public async Task OwnedPageLinkNoticeIsDismissedWithoutContinueOrFabricatedId()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Page = true;
        d.PageLinkControl = true;
        d.PageLinkNotice = true;
        d.RevealAfter = 1;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).ReadNumericIdAsync(new("Fixture Owner", "Page"), default);
        Assert.Null(id);
        Assert.Equal(1, d.PageLinkTaps);
        Assert.Equal(2, d.BackCount);
        Assert.False(d.Settings);
    }

    [Fact]
    public async Task ChangedProcessAfterSettingsTapCannotProduceIdentity()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.ChangePid = true;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeFetchDevice(
                ld,
                901,
                "fixture",
                _ => Task.CompletedTask
            ).ReadNumericIdAsync(new("Fixture Owner", "Profile"), default)
        );
        Assert.Equal(0, d.Forbidden);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("foreign")]
    [InlineData("empty")]
    public async Task UnsafeScreenCannotStartNativeFetchNavigation(string mutation)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Mutation = mutation;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeFetchDevice(ld, 901, "fixture", _ => Task.CompletedTask).ScanAsync(
                default
            )
        );
        Assert.Equal(0, d.SettingsTaps + d.Forbidden);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("different-notice")]
    public void LinkNoticeRequiresExactSafePromptAndUniqueUsableContinue(string mode)
    {
        var doc = XDocument.Parse(
            "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node package='com.facebook.katana' text='Link sharing has been updated'/><node package='com.facebook.katana' text='Links you share are unique to you and may be used to improve suggestions, if ads are shown.'/><node package='com.facebook.katana' text='Continue' clickable='true' enabled='true' bounds='[20,900][700,980]'/></node></hierarchy>"
        );
        var button = doc.Descendants("node").Single(n => n.Attribute("text")?.Value == "Continue");
        if (mode == "disabled")
            button.SetAttributeValue("enabled", "false");
        if (mode == "duplicate")
            button.Parent!.Add(new XElement(button));
        if (mode == "foreign")
            button.SetAttributeValue("package", "other.app");
        if (mode == "input")
            button.SetAttributeValue("class", "android.widget.EditText");
        if (mode == "different-notice")
            doc.Descendants("node")
                .Single(n => n.Attribute("text")?.Value == "Link sharing has been updated")
                .SetAttributeValue("text", "Other notice");
        Assert.Null(PFlowNativeFetchDevice.LinkNoticeContinue(doc.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactOwnedLinkNoticeContinuesOnceAndReadsFreshLink(bool staysOnNotice)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Page = true;
        d.PageLinkControl = true;
        d.PageLinkNotice = true;
        d.ExactNotice = true;
        d.StaysOnNotice = staysOnNotice;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).ReadNumericIdAsync(new("Fixture Owner", "Page"), default);
        Assert.Equal(staysOnNotice ? null : "123456789012345", id);
        Assert.Equal(1, d.ContinueTaps);
        Assert.Equal(1, d.PageLinkTaps);
        Assert.False(d.Settings);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task PersonalCopyNoticeIsHandledBeforeClipboardPolling(bool exact, bool stuck)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.ProfileCopyControl = d.ClipboardReady = d.PageLinkNotice = true;
        d.CopiedLink = "https://www.facebook.com/profile.php?id=123456789012345";
        d.ExactNotice = exact;
        d.StaysOnNotice = stuck;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask
        ).ReadNumericIdAsync(new("Fixture Owner", "Profile"), default);
        Assert.Equal(exact && !stuck ? "123456789012345" : null, id);
        Assert.Equal(exact ? 1 : 0, d.ContinueTaps);
        Assert.Equal(exact && !stuck ? 3 : 0, d.ClipboardReads);
        Assert.False(d.Settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshCopiedPageLinkUsesNumericOrBoundedPublicResolver(bool shortLink)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Page = true;
        d.PageLinkControl = true;
        d.ClipboardReady = true;
        d.CopiedLink = shortLink ? "https://www.facebook.com/share/ExampleLink/" : d.Link;
        var resolved = 0;
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask,
            (link, _) =>
            {
                Assert.Equal(d.CopiedLink, link);
                resolved++;
                return Task.FromResult<string?>("123456789012345");
            }
        ).ReadNumericIdAsync(new("Fixture Owner", "Page"), default);
        Assert.Equal("123456789012345", id);
        Assert.Equal(shortLink ? 1 : 0, resolved);
        Assert.True(d.ClipboardReads >= 3);
        Assert.False(d.Settings);
    }

    [Fact]
    public async Task SharePageRequiresSeparateCopyBeforeResolving()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, UiDevice>();
        var d = (UiDevice)(object)ld;
        d.Page = d.ShareControl = d.ClipboardReady = true;
        d.CopiedLink = "https://www.facebook.com/share/ExampleLink/";
        using var scope = new PFlowNativeFetchScope(ld, 901, "fixture");
        var calls = 0;
        var id = await new PFlowNativeFetchDevice(
            ld,
            901,
            "fixture",
            _ => Task.CompletedTask,
            (link, _) =>
            {
                calls++;
                Assert.True(d.ShareCopied);
                Assert.Equal(d.CopiedLink, link);
                return Task.FromResult<string?>("123456789012345");
            }
        ).ReadNumericIdAsync(new("Fixture Owner", "Page"), default);
        Assert.Equal("123456789012345", id);
        Assert.Equal(1, calls);
        Assert.Equal(0, d.Swipes);
    }

    public class RecoveryDevice : DispatchProxy
    {
        public int PrivateReads;
        public string Destination = "";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "SendKeyAsync")
                return Task.FromResult(true);
            if (method.Name == "EnsureSwitchedToPersonalProfileAsync")
            {
                Destination = (string)args![1]!;
                return Task.FromResult(true);
            }
            PrivateReads++;
            throw new Exception("Recovery attempted private account extraction");
        }
    }

    public class SwitchDevice : DispatchProxy
    {
        bool _sheet = true;
        public string? OwnCapture;
        public string Pid = "123";
        public bool DetailsHandoff;
        public int Backs;
        public bool WrongDestination;
        public int SheetOpens,
            Forbidden;

        string Xml()
        {
            if (!_sheet)
                return OwnCapture
                    ?? FacebookLayoutRegistryTests.Fixture("ld10-posting-menu-captured");
            var doc = System.Xml.Linq.XDocument.Load(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowScreenTime",
                    "ld55-switcher-selected-page-captured.xml"
                )
            );
            if (WrongDestination && SheetOpens > 0)
            {
                foreach (var n in doc.Descendants("node"))
                    n.SetAttributeValue("selected", "false");
                var personal = doc.Descendants("node")
                    .Single(n => n.Attribute("content-desc")?.Value == "Personal Owner");
                personal.SetAttributeValue("selected", "true");
                var page = doc.Descendants("node")
                    .Single(n => n.Attribute("content-desc")?.Value == "Bushcraft improvisation");
                page.Add(
                    new System.Xml.Linq.XElement(
                        "node",
                        new System.Xml.Linq.XAttribute("package", "com.facebook.katana"),
                        new System.Xml.Linq.XAttribute("text", "Page")
                    )
                );
            }
            return doc.ToString();
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "ExtractPageInfoFromUiAsync":
                    return ReadDetailsAsync(args!);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml())
                    );
                case "ExecuteAdbAsync":
                    if ((string)args![1]! == "shell pidof com.facebook.katana")
                        return Task.FromResult(Pid);
                    break;
                case "PressBackAsync":
                    Backs++;
                    _sheet = false;
                    return Task.FromResult(true);
                case "TapAsync":
                    _sheet = !_sheet;
                    if (_sheet)
                        SheetOpens++;
                    return Task.FromResult(true);
            }
            Forbidden++;
            throw new NotSupportedException(method.Name);
        }

        private async Task<FacebookPageInfo?> ReadDetailsAsync(object?[] args)
        {
            DetailsHandoff =
                await PFlowFetchSelectionHandoff.TryConsumeAsync(
                    (ILDPlayerService)(object)this,
                    (int)args[0]!,
                    (string?)args[6],
                    (string)args[3]!,
                    (string)args[2]!,
                    (string)args[4]!,
                    (CancellationToken)args[7]!
                ) == true;
            return new FacebookPageInfo
            {
                PageName = (string)args[3]!,
                PersonaUid = (string)args[1]!,
                PageId = (string)args[2]!,
            };
        }
    }

    public class UiDevice : DispatchProxy
    {
        public string? SettingsCapture;
        public int FeedRequests;
        public string? OwnCapture;
        public bool Page,
            Settings,
            ChangePid;
        public int SettingsTaps,
            Forbidden;
        public (int X, int Y) LastTap;
        public int RevealAfter,
            Swipes;
        public bool ChangePidAfterScroll;
        public bool PageLinkControl,
            PageLinkOpened;
        public int PageLinkTaps;
        public bool PageLinkNotice;
        public bool ExactNotice,
            NoticeContinued,
            StaysOnNotice;
        public int ContinueTaps;
        public bool ClipboardReady;
        public bool ProfileCopyControl,
            ProfileCopied;
        public bool ShareControl,
            ShareSheet,
            ShareCopied;
        public string? CopiedLink;
        public int ClipboardReads;
        public int BackCount;
        public string Link = "https://www.facebook.com/profile.php?id=123456789012345",
            Mutation = "";

        static string Node(
            string label,
            string bounds,
            string cls = "android.widget.TextView",
            bool clickable = false
        ) =>
            $"<node package='com.facebook.katana' class='{cls}' text='{label}' content-desc='{label}' enabled='true' clickable='{clickable.ToString().ToLowerInvariant()}' bounds='{bounds}'/>";

        string Xml()
        {
            if (Settings && SettingsCapture != null)
                return SettingsCapture;
            if (FeedRequests > 0)
                return FacebookLayoutRegistryTests.Fixture("ld42-fetch-recovery-feed");
            if (ShareSheet)
                return "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'>"
                    + Node("Copy link", "[0,900][200,1000]", "android.widget.Button", true)
                    + "</node></hierarchy>";
            if (Mutation == "empty")
                return "<hierarchy/>";
            if (!Settings && OwnCapture != null)
                return OwnCapture;
            if (
                Settings
                && PageLinkNotice
                && (PageLinkOpened || ProfileCopied)
                && (!NoticeContinued || StaysOnNotice)
            )
                return "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'>"
                    + Node("Link sharing has been updated", "[0,200][700,250]")
                    + (
                        ExactNotice
                            ? Node(
                                "Links you share are unique to you and may be used to improve suggestions, if ads are shown.",
                                "[0,260][700,400]"
                            )
                            : ""
                    )
                    + Node("Continue", "[0,500][700,560]", "android.widget.Button", true)
                    + "</node></hierarchy>";
            var xml =
                "<hierarchy><node package='com.facebook.katana' class='android.widget.FrameLayout' bounds='[0,0][720,1280]'>"
                + (
                    Settings
                        ? Node(Page ? "Page Settings" : "Profile settings", "[0,0][400,60]")
                            + Node(Page ? "Page status" : "View as", "[0,70][500,130]")
                            + (
                                Swipes < RevealAfter ? ""
                                : ProfileCopyControl
                                    ? Node(
                                        "Copy profile link",
                                        "[0,200][500,250]",
                                        "android.widget.Button",
                                        true
                                    )
                                : ShareControl
                                    ? Node(
                                        "Share Page",
                                        "[0,200][500,250]",
                                        "android.widget.Button",
                                        true
                                    )
                                : PageLinkControl && !PageLinkOpened
                                    ? Node(
                                        "Page link",
                                        "[0,200][500,250]",
                                        "android.widget.Button",
                                        true
                                    )
                                : Node(
                                    Page ? "Your Page link" : "Your profile link",
                                    "[0,200][500,250]"
                                ) + Node(Link, "[0,260][700,320]")
                            )
                        : Node("Fixture Owner", "[0,100][500,160]")
                            + Node(
                                "Edit profile",
                                "[0,300][250,360]",
                                "android.widget.Button",
                                true
                            )
                            + Node(
                                "Creator Dashboard",
                                "[0,400][300,460]",
                                "android.widget.Button",
                                true
                            )
                            + Node(
                                "Profile settings",
                                "[600,60][700,120]",
                                "android.widget.Button",
                                true
                            )
                )
                + (
                    Mutation == "input"
                        ? "<node package='com.facebook.katana' class='android.widget.EditText' text='User draft'/>"
                        : ""
                )
                + "</node></hierarchy>";
            return Mutation == "foreign" ? xml.Replace("com.facebook.katana", "other.app") : xml;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var value in args ?? [])
                if (value is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "ExecuteAdbAsync":
                    if ((string)args![1]! == "shell pidof com.facebook.katana")
                        return Task.FromResult(
                            Settings && (ChangePid || ChangePidAfterScroll && Swipes > 0)
                                ? "456"
                                : "123"
                        );
                    Forbidden++;
                    throw new Exception(
                        "Root-disabled test device rejects private/root/Graph/other shell reads"
                    );
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>("com.facebook.katana/Main");
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml());
                case "OpenDeepLinkAsync":
                    Assert.Equal("fb://feed", args![1]);
                    Assert.Equal("com.facebook.katana", args[2]);
                    FeedRequests++;
                    return Task.FromResult(true);
                case "PrepareProfileLinkClipboardAsync":
                    return Task.FromResult(ClipboardReady);
                case "ReadCopiedProfileLinkAsync":
                    Assert.True(ClipboardReady);
                    ClipboardReads++;
                    return Task.FromResult(
                        (
                            ProfileCopyControl ? ProfileCopied
                            : ShareControl ? ShareCopied
                            : PageLinkOpened
                        )
                            ? CopiedLink
                            : null
                    );
                case "TapAsync":
                    if (Settings && PageLinkNotice && (PageLinkOpened || ProfileCopied))
                    {
                        ContinueTaps++;
                        NoticeContinued = true;
                        return Task.FromResult(true);
                    }
                    if (Settings && ProfileCopyControl)
                    {
                        ProfileCopied = true;
                        return Task.FromResult(true);
                    }
                    if (Settings && ShareControl)
                    {
                        if (ShareSheet)
                        {
                            ShareCopied = true;
                            ShareSheet = false;
                        }
                        else
                            ShareSheet = true;
                        return Task.FromResult(true);
                    }
                    if (Settings && PageLinkControl)
                    {
                        PageLinkOpened = true;
                        PageLinkTaps++;
                        return Task.FromResult(true);
                    }
                    SettingsTaps++;
                    LastTap = ((int)args![1]!, (int)args[2]!);
                    Settings = true;
                    return Task.FromResult(true);
                case "PressBackAsync":
                    BackCount++;
                    if (
                        PageLinkNotice
                        && (PageLinkOpened || ProfileCopied)
                        && (!NoticeContinued || StaysOnNotice)
                    )
                    {
                        PageLinkOpened = ProfileCopied = false;
                        return Task.FromResult(true);
                    }
                    Settings = false;
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Assert.True(Settings);
                    Swipes++;
                    return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
