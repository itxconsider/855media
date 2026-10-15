using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

// Constructed navigation fixtures. These do not certify a new installed Facebook layout.
public sealed class PFlowNativeAccountTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "pflow-rotation-native-" + Guid.NewGuid().ToString("N")
    );
    private const int Index = 900181;

    private PFlowAccountBinding Binding(string uid = "123") =>
        new(
            PFlowDeviceRouting.ReadIdentity(_directory, Index),
            Index,
            "fixture-LD",
            "com.facebook.katana",
            uid
        );

    public PFlowNativeAccountTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(_directory, "vms", "leidian" + Index));
        File.WriteAllText(
            Path.Combine(_directory, "vms", "config", "leidian" + Index + ".config"),
            "{\"propertySettings.phoneAndroidId\":\"fixture-rotation\"}"
        );
        File.WriteAllText(
            Path.Combine(_directory, "vms", "leidian" + Index, "data.vmdk"),
            "fixture"
        );
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private static XElement Node(
        string label,
        bool clickable = false,
        string bounds = "[0,0][200,60]"
    ) =>
        new(
            "node",
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute("class", "android.widget.Button"),
            new XAttribute("text", label),
            new XAttribute("content-desc", label),
            new XAttribute("enabled", "true"),
            new XAttribute("clickable", clickable ? "true" : "false"),
            new XAttribute("bounds", bounds)
        );

    private static string Xml(params XElement[] nodes) =>
        new XElement(
            "hierarchy",
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("bounds", "[0,0][720,1280]"),
                nodes
            )
        ).ToString();

    private static string Settings(string url = "https://www.facebook.com/profile.php?id=123") =>
        Xml(Node("Profile settings"), Node("Your profile link"), Node(url));

    private static string Own() =>
        Xml(
            Node("Fixture Personal"),
            Node("Edit profile", true),
            Node("Edit cover photo button", true),
            Node("Edit profile picture", true),
            Node("Profile settings", true, "[250,100][350,160]")
        );

    private static string ProfessionalSettings(bool clipped = false) =>
        Xml(
            Node("Profile Settings"),
            Node("Back", true),
            Node("Activity log"),
            Node("Copy profile link", true, clipped ? "[0,1276][720,1280]" : "[10,200][250,260]"),
            Node("Profile and tagging settings")
        );

    [Fact]
    public void RepeatedEditProfileControlsRecognizeOwnProfileWithoutSelectingAnAmbiguousTap()
    {
        var xml = Xml(
            Node("Edit profile", true, "[472,72][544,144]"),
            Node("Edit profile", true, "[378,925][696,997]"),
            Node("More profile settings", true, "[616,72][688,144]")
        );
        Assert.True(PFlowNativeAccountNavigator.OwnProfile(xml));
        Assert.Null(PFlowNativeAccountNavigator.FindControl(xml, "Edit profile"));
        Assert.NotNull(PFlowNativeAccountNavigator.FindControl(xml, "More profile settings"));
        Assert.False(
            PFlowNativeAccountNavigator.OwnProfile(
                Xml(Node("Manage Page"), Node("Edit profile", true))
            )
        );
    }

    [Fact]
    public void CoverPhotoLabelVariantRecognizesOwnProfile()
    {
        Assert.True(
            PFlowNativeAccountNavigator.OwnProfile(
                Xml(
                    Node("Edit cover photo", true),
                    Node("Edit profile picture", true),
                    Node("More profile settings", true)
                )
            )
        );
        Assert.False(
            PFlowNativeAccountNavigator.OwnProfile(Xml(Node("More profile settings", true)))
        );
    }

    [Fact]
    public void LinkNoticeDismissalRequiresSpecificTitleAndCloseControl()
    {
        Assert.True(
            PFlowNativeAccountNavigator.LinkSharingNotice(
                Xml(Node("Link sharing has been updated"), Node("Close", true))
            )
        );
        Assert.False(
            PFlowNativeAccountNavigator.LinkSharingNotice(
                Xml(Node("Close", true), Node("Continue", true))
            )
        );
        Assert.False(
            PFlowNativeAccountNavigator.LinkSharingNotice(
                Xml(Node("Link sharing has been updated"))
            )
        );
    }

    [Fact]
    public void PersonalDetailsRecoveryRequiresEditableProfileSheetContext()
    {
        var ownSheet = Xml(
            Node("Transparency and privacy policy"),
            Node("Edit field"),
            Node("Close", true)
        );
        Assert.True(PFlowNativeAccountNavigator.PersonalDetailsSheet(ownSheet));
        Assert.True(PFlowNativeAccountNavigator.CanRecoverPersonalProfile(ownSheet));
        Assert.False(
            PFlowNativeAccountNavigator.PersonalDetailsSheet(
                Xml(Node("Transparency and privacy policy"), Node("Close", true))
            )
        );
        Assert.True(
            PFlowNativeAccountNavigator.CanRecoverPersonalProfile(
                Xml(Node("Profile transparency"), Node("Back", true))
            )
        );
    }

    [Theory]
    [InlineData("notice")]
    [InlineData("details")]
    [InlineData("transparency")]
    public async Task ExistingProfileOverlaysReturnToLiveUidVerification(string surface)
    {
        var (nav, fake) = Native();
        fake.Ui = surface switch
        {
            "notice" => Xml(Node("Link sharing has been updated"), Node("Close", true)),
            "details" => Xml(
                Node("Transparency and privacy policy"),
                Node("Edit field"),
                Node("Close", true)
            ),
            _ => Xml(Node("Profile transparency"), Node("Back", true), Node("Page ID 999")),
        };
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal("123", observed.OwnerUid);
        Assert.Equal(2, fake.Backs);
        Assert.Equal(0, fake.LoginSubmissions);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("999", false)]
    public async Task EmbeddedProfileRouteRequiresFreshUidInsteadOfTrustingDestination(
        string liveUid,
        bool expected
    )
    {
        var (nav, fake) = Native();
        fake.Ui = Xml(Node("Edit cover photo", true), Node("Edit profile picture", true));
        fake.EmbeddedProfile = true;
        fake.Link = "https://www.facebook.com/profile.php?id=" + liveUid;
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.Equal(expected, PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(liveUid, observed.OwnerUid);
        Assert.Single(fake.Calls, c => c == "OpenDeepLinkAsync");
        Assert.Equal(0, fake.LoginSubmissions);
    }

    [Fact]
    public void VisibleUsernameAndGenericCopyLinkRequireOwnProfileSettingsContext()
    {
        var xml = Xml(
            Node("Profile Settings"),
            Node("Your profile link"),
            Node("https://www.facebook.com/fixture.username"),
            Node("Copy link", true)
        );
        Assert.Equal(
            "https://www.facebook.com/fixture.username",
            PFlowNativeAccountNavigator.ReadOwnProfileLink(xml)
        );
        Assert.Null(PFlowNativeAccountNavigator.ReadOwnProfileUid(xml));
        Assert.NotNull(PFlowNativeAccountNavigator.CopyProfileLinkControl(xml));
        Assert.Null(
            PFlowNativeAccountNavigator.CopyProfileLinkControl(
                Xml(Node("Profile Settings"), Node("Copy link", true))
            )
        );
        Assert.Null(
            PFlowNativeAccountNavigator.ReadOwnProfileLink(
                Xml(
                    Node("Page Settings"),
                    Node("Your Page link"),
                    Node("https://www.facebook.com/fixture.username"),
                    Node("Copy link", true)
                )
            )
        );
    }

    [Fact]
    public async Task ProfessionalModeCopiesFreshNumericUidThenRestoresOwnProfile()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.UseCopySettings = true;
        fake.CopiedProfileLink = "https://www.facebook.com/profile.php?id=123";
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(1, fake.CopyTaps);
        Assert.Equal(Own(), fake.Ui);
        Assert.True(
            fake.Calls.IndexOf("PrepareProfileLinkClipboardAsync")
                < fake.Calls.IndexOf("ReadCopiedProfileLinkAsync")
        );
        Assert.Equal(0, fake.LoginSubmissions);
        Assert.All(fake.Indices, index => Assert.Equal(Index, index));
    }

    [Fact]
    public async Task ClippedCopyRowIsScrolledBeforeClipboardResetAndTap()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.UseCopySettings = true;
        fake.CopyStartsClipped = true;
        fake.CopiedProfileLink = "https://www.facebook.com/profile.php?id=123";
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(1, fake.CopyTaps);
        Assert.True(
            fake.Calls.IndexOf("SwipeAsync")
                < fake.Calls.IndexOf("PrepareProfileLinkClipboardAsync")
        );
        Assert.Equal(Own(), fake.Ui);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task StaleClipboardCannotProveIdentity(bool resetWorks, bool copyUpdates)
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.UseCopySettings = true;
        fake.ProfileClipboard = "https://www.facebook.com/profile.php?id=123";
        fake.ClipboardResetWorks = resetWorks;
        fake.CopyUpdatesClipboard = copyUpdates;
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.False(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal("", observed.OwnerUid);
        if (!resetWorks)
        {
            Assert.Equal(0, fake.CopyTaps);
            Assert.DoesNotContain("ReadCopiedProfileLinkAsync", fake.Calls);
        }
    }

    [Fact]
    public async Task CopiedWrongUidIsReportedWithoutSubstitution()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.UseCopySettings = true;
        fake.CopiedProfileLink = "https://www.facebook.com/456";
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.Equal("456", observed.OwnerUid);
        Assert.False(PFlowAutomaticSessionService.Verified(observed, Binding()));
    }

    [Theory]
    [InlineData("https://www.facebook.com/profile.php?id=123", "123")]
    [InlineData("https://m.facebook.com/123/?ref=profile", "123")]
    [InlineData("https://facebook.com/people/Fixture/123/", "123")]
    [InlineData("https://www.facebook.com/fixture.username", null)]
    [InlineData("https://facebook.com.evil.test/profile.php?id=123", null)]
    [InlineData("https://www.facebook.com/profile.php?id=123&id=456", null)]
    [InlineData("https://user@www.facebook.com/123", null)]
    [InlineData("pflow-profile-proof-marker", null)]
    public void CopiedLinkRequiresAnUnambiguousFacebookNumericUid(string link, string? expected) =>
        Assert.Equal(expected, PFlowNativeAccountNavigator.ReadNumericProfileLinkUid(link));

    [Fact]
    public void CopyRouteRejectsPageAndEditableSettings()
    {
        Assert.NotNull(PFlowNativeAccountNavigator.CopyProfileLinkControl(ProfessionalSettings()));
        Assert.Null(
            PFlowNativeAccountNavigator.CopyProfileLinkControl(
                ProfessionalSettings().Replace("Profile Settings", "Page Settings")
            )
        );
        var input = Node("Password");
        input.SetAttributeValue("class", "android.widget.EditText");
        Assert.Null(
            PFlowNativeAccountNavigator.CopyProfileLinkControl(
                Xml(Node("Profile Settings"), Node("Copy profile link", true), input)
            )
        );
    }

    [Theory]
    [InlineData("[0,1276][720,1280]")]
    [InlineData("[0,1235][720,1280]")]
    [InlineData("[0,1400][720,1500]")]
    public void CopyRouteRejectsClippedOrOffscreenRows(string bounds)
    {
        Assert.Null(
            PFlowNativeAccountNavigator.CopyProfileLinkControl(
                Xml(
                    Node("Profile Settings"),
                    Node("Back", true),
                    Node("Copy profile link", true, bounds)
                )
            )
        );
    }

    [Fact]
    public void CopyRouteAcceptsVisibleRowAfterScrolling()
    {
        Assert.Equal(
            (360, 1050),
            PFlowNativeAccountNavigator.CopyProfileLinkControl(
                Xml(
                    Node("Profile Settings"),
                    Node("Back", true),
                    Node("Copy profile link", true, "[0,1000][720,1100]")
                )
            )
        );
    }

    [Fact]
    public void ObservedLd12SettingsDoesNotTapFourPixelCopySliver()
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld12-profile-settings-clipped-copy.xml"
            )
        );
        Assert.Null(PFlowNativeAccountNavigator.CopyProfileLinkControl(xml));
    }

    private static XElement TextButton(string text, string bounds)
    {
        var node = Node(text, true, bounds);
        node.SetAttributeValue("content-desc", "");
        return node;
    }

    private static string SaveInfo() =>
        Xml(
            Node("Save your login info?"),
            TextButton("NOT NOW", "[316,745][490,841]"),
            TextButton("SAVE", "[490,745][618,841]")
        );

    private static string LogoutConfirm() =>
        Xml(
            Node("Log out of your account?"),
            TextButton("CANCEL", "[323,650][477,746]"),
            TextButton("LOG OUT", "[477,650][638,746]")
        );

    private static string SavedLanding() =>
        Xml(
            Node("Facebook from Meta"),
            Node("Fixture Personal"),
            Node("Continue", true),
            Node("Use another profile", true, "[209,748][512,812]"),
            Node("Create new account", true)
        );

    [Theory]
    [InlineData("https://www.facebook.com/profile.php?id=123", "123")]
    [InlineData("https://m.facebook.com/profile.php?id=456&ref=profile", "456")]
    [InlineData("https://www.facebook.com/people/Fixture-Personal/789/", "789")]
    [InlineData("https://facebook.com/profile.php?id=00123", "00123")]
    [InlineData("https://www.facebook.com/fixture.username", null)]
    [InlineData("https://www.facebook.com/profile.php?id=123&id=456", null)]
    [InlineData("https://www.facebook.com/profile.php?id=non-numeric", null)]
    [InlineData("https://www.facebook.com.evil.invalid/profile.php?id=123", null)]
    [InlineData("https://evil.invalid/profile.php?id=123", null)]
    public void ReadsOnlyUnambiguousNumericPersonalLink(string url, string? expected) =>
        Assert.Equal(expected, PFlowNativeAccountNavigator.ReadOwnProfileUid(Settings(url)));

    [Fact]
    public void FeedPublicProfileAndPageLinksAreNotIdentityEvidence()
    {
        Assert.Null(
            PFlowNativeAccountNavigator.ReadOwnProfileUid(
                Xml(Node("https://www.facebook.com/profile.php?id=123"))
            )
        );
        Assert.Null(
            PFlowNativeAccountNavigator.ReadOwnProfileUid(
                Settings().Replace("Your profile link", "Your Page link")
            )
        );
        var doc = XDocument.Parse(Settings());
        doc.Root!.Add(Node("https://www.facebook.com/profile.php?id=456"));
        Assert.Null(PFlowNativeAccountNavigator.ReadOwnProfileUid(doc.ToString()));
        Assert.Null(
            PFlowNativeAccountNavigator.ReadOwnProfileUid(
                Settings().Replace("com.facebook.katana", "com.other.app")
            )
        );
    }

    [Fact]
    public void LoginOrEditableOverlayCannotSupplyUid()
    {
        var doc = XDocument.Parse(Settings());
        var input = Node("fixture");
        input.SetAttributeValue("class", "android.widget.EditText");
        doc.Root!.Add(input);
        Assert.Null(PFlowNativeAccountNavigator.ReadOwnProfileUid(doc.ToString()));
        Assert.Null(PFlowNativeAccountNavigator.ReadOwnProfileUid("malformed"));
    }

    [Fact]
    public void DisabledAmbiguousOrMispackagedLogoutControlsAreRejected()
    {
        Assert.NotNull(
            PFlowNativeAccountNavigator.FindControl(Xml(Node("Log out", true)), "Log out")
        );
        var node = Node("Log out", true);
        node.SetAttributeValue("enabled", "false");
        Assert.Null(PFlowNativeAccountNavigator.FindControl(Xml(node), "Log out"));
        Assert.Null(
            PFlowNativeAccountNavigator.FindControl(
                Xml(Node("Log out", true), Node("Log out", true, "[0,200][200,260]")),
                "Log out"
            )
        );
        Assert.Null(
            PFlowNativeAccountNavigator.FindControl(
                Xml(Node("Log out", true)).Replace("com.facebook.katana", "com.other.app"),
                "Log out"
            )
        );
    }

    private (PFlowNativeAccountNavigator Navigator, Device Fake) Native(
        Func<PFlowAccountBinding, CancellationToken, Task>? requireRecoveryIdle = null,
        Func<PFlowAccountBinding, CancellationToken, Task<PFlowGraphIdentity?>>? graphIdentity =
            null
    )
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        return (
            new(
                ld,
                _directory,
                _ => "Fixture Personal",
                delay: (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                requireRecoveryIdle: requireRecoveryIdle,
                graphIdentity: graphIdentity
            ),
            fake
        );
    }

    [Fact]
    public async Task TemporaryMissingInstanceListRetriesTheSameAssignedDevice()
    {
        var (nav, fake) = Native();
        fake.MissingInstanceReads = 2;
        var worker = await nav.ReadWorkerAsync(Binding(), default);
        Assert.Equal(Index, worker.DeviceIndex);
        Assert.Equal(3, fake.InstanceReads);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task MatchingPersonalGraphIdentitySkipsProfileSettings()
    {
        var (nav, fake) = Native(
            graphIdentity: (_, _) =>
                Task.FromResult<PFlowGraphIdentity?>(
                    new("123", "Fixture Personal", "123", "https://example.test/picture.jpg")
                )
        );
        fake.Ui = Own();
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, fake.Backs);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("page")]
    [InlineData("name")]
    [InlineData("owner")]
    public async Task MissingOrConflictingGraphIdentityUsesOldSettingsLookup(string mode)
    {
        var graph = mode switch
        {
            "missing" => null,
            "page" => new PFlowGraphIdentity("456", "Fixture Personal", "123", null),
            "name" => new PFlowGraphIdentity("123", "Other person", "123", null),
            _ => new PFlowGraphIdentity("123", "Fixture Personal", "456", null),
        };
        var (nav, fake) = Native(graphIdentity: (_, _) => Task.FromResult(graph));
        fake.Ui = Own();
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.True(fake.Taps > 0);
        Assert.True(fake.Backs > 0);
    }

    [Fact]
    public async Task PersistentlyMissingAssignedDeviceStopsAfterThreeChecks()
    {
        var (nav, fake) = Native();
        fake.MissingInstanceReads = 10;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.ReadWorkerAsync(Binding(), default)
        );
        Assert.Contains("three instance-list checks", error.Message);
        Assert.Equal(3, fake.InstanceReads);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, fake.UidRecoveryStops);
    }

    [Fact]
    public async Task LiveObservationNavigatesOwnSettingsThenRestoresOwnProfile()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.Equal("123", observed.OwnerUid);
        Assert.True(observed.IsLiveUid);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(Own(), fake.Ui);
        Assert.Equal(1, fake.Taps);
        Assert.Equal(1, fake.Backs);
        Assert.All(fake.Indices, index => Assert.Equal(Index, index));
    }

    [Fact]
    public async Task WrongOwnerIsReportedRatherThanReplacedByRequestedUid()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        var observed = await nav.ObserveAsync(Binding("456"), default);
        Assert.Equal("123", observed.OwnerUid);
        Assert.False(PFlowAutomaticSessionService.Verified(observed, Binding("456")));
    }

    [Fact]
    public async Task FailedOwnIdentityProofRecoversMappedPersonalProfileAndVerifiesFreshUid()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.Link = "https://www.facebook.com/fixture.page";
        fake.SettingsHasBack = true;
        fake.RecoveredUid = "123";
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(1, fake.PersonalRecoveries);
        Assert.Equal(0, fake.LoginSubmissions);
        Assert.Equal(0, fake.MenuLogouts);
        Assert.All(fake.Indices, index => Assert.Equal(Index, index));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("456")]
    public async Task FailedOrWrongRecoveryCannotAuthenticateTheMappedUid(string? recoveredUid)
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.Link = "https://www.facebook.com/fixture.page";
        fake.SettingsHasBack = true;
        fake.RecoveredUid = recoveredUid;
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.False(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(recoveredUid == null ? 2 : 1, fake.PersonalRecoveries);
        Assert.Equal(recoveredUid == null ? 1 : 0, fake.UidRecoveryStops);
        Assert.Equal(0, fake.LoginSubmissions);
        Assert.Equal(0, fake.MenuLogouts);
        if (recoveredUid != null)
            Assert.Equal(recoveredUid, observed.OwnerUid);
    }

    [Fact]
    public void AutomaticProfileRecoveryRejectsLoginInputsAndUnknownScreens()
    {
        Assert.False(PFlowNativeAccountNavigator.CanRecoverPersonalProfile(SavedLanding()));
        Assert.False(PFlowNativeAccountNavigator.CanRecoverPersonalProfile(Xml(Node("Loading"))));
        var input = Node("Password");
        input.SetAttributeValue("class", "android.widget.EditText");
        Assert.False(
            PFlowNativeAccountNavigator.CanRecoverPersonalProfile(
                Xml(Node("Edit profile", true), input)
            )
        );
        Assert.True(PFlowNativeAccountNavigator.CanRecoverPersonalProfile(Own()));
    }

    [Fact]
    public async Task MissingNumericLinkNeverFallsBackToCachedOwner()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.Link = "https://www.facebook.com/fixture.username";
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.False(observed.IsLiveUid);
        Assert.Equal("", observed.OwnerUid);
        Assert.Equal(PFlowAuthenticationState.Unavailable, observed.State);
        Assert.Equal(1, fake.UidRecoveryStops);
        Assert.Equal(1, fake.UidRecoveryLaunches);
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("456", false)]
    public async Task MissingUidClearsRecentsReopensAndReadsFreshIdentity(
        string uid,
        bool expectedVerified
    )
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.Link = "https://www.facebook.com/fixture.page";
        fake.UidAfterRelaunch = uid;
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.Equal(expectedVerified, PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(uid, observed.OwnerUid);
        Assert.Equal(1, fake.UidRecoveryStops);
        Assert.Equal(1, fake.UidRecoveryLaunches);
        Assert.Contains("ExecuteAdbAsync", fake.Calls);
        Assert.True(
            fake.Calls.IndexOf("StopFacebookAppAsync") < fake.Calls.IndexOf("LaunchAppAsync")
        );
        Assert.Equal(0, fake.LoginSubmissions);
        Assert.Equal(0, fake.MenuLogouts);
        Assert.All(fake.Indices, index => Assert.Equal(Index, index));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedRecentClearOrReopenStopsUidRecovery(bool clearFails)
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.Link = "https://www.facebook.com/fixture.page";
        fake.RecentAppsVerifiedEmpty = !clearFails;
        fake.RelaunchSucceeds = clearFails;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.ObserveAsync(Binding(), default)
        );
        Assert.Equal(1, fake.UidRecoveryStops);
        Assert.Equal(clearFails ? 0 : 1, fake.UidRecoveryLaunches);
    }

    [Fact]
    public async Task PendingWorkflowReviewBlocksClearingRecentApps()
    {
        var (nav, fake) = Native(
            (_, _) => throw new InvalidOperationException("Pending publication")
        );
        fake.Ui = Own();
        fake.Link = "https://www.facebook.com/fixture.page";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.ObserveAsync(Binding(), default)
        );
        Assert.Equal(0, fake.UidRecoveryStops);
        Assert.Equal(0, fake.UidRecoveryLaunches);
    }

    [Fact]
    public async Task LoginLandingDoesNotClearRecentAppsForUidRecovery()
    {
        var (nav, fake) = Native();
        fake.Ui = SavedLanding();
        var observed = await nav.ObserveAsync(Binding(), default);
        Assert.Equal(PFlowAuthenticationState.LoginRequired, observed.State);
        Assert.Equal(0, fake.UidRecoveryStops);
    }

    [Fact]
    public async Task ChangedForegroundStopsBeforeAnyTap()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.Foreground = "com.other.app/ProfileActivity";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.ObserveAsync(Binding(), default)
        );
        Assert.Equal(0, fake.Taps);
    }

    [Theory]
    [InlineData("Use your saved password for Facebook")]
    [InlineData("Save password to sign in to Facebook?")]
    public async Task AndroidCredentialPromptIsClosedBeforeReadingFacebook(string title)
    {
        var (_, fake) = Native();
        var ld = (ILDPlayerService)(object)fake;
        fake.Foreground = PFlowAndroidCredentialPrompt.Activity;
        fake.Ui = PFlowAndroidCredentialPromptTests.Prompt(title);
        var xml = await new PFlowSessionPreparation(ld, _directory).ReadFacebookScreenAsync(
            Binding(),
            default
        );
        Assert.Equal(Own(), xml);
        Assert.Equal(1, fake.Taps);
        Assert.Equal(0, fake.Inputs);
        Assert.Equal(0, fake.LoginSubmissions);
    }

    [Theory]
    [InlineData("")]
    [InlineData(PFlowAndroidCredentialPrompt.Activity)]
    public async Task KnownLifecycleTransitionWaitsWithoutClicking(string activity)
    {
        var (_, fake) = Native();
        var ld = (ILDPlayerService)(object)fake;
        fake.Ui = Own();
        fake.ForegroundSequence.Enqueue(activity);
        fake.ForegroundSequence.Enqueue("com.facebook.katana/ProfileActivity");
        var xml = await new PFlowSessionPreparation(ld, _directory).ReadFacebookScreenAsync(
            Binding(),
            default
        );
        Assert.Equal(Own(), xml);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task SubmittedLoginFormCanLoadBeforeAuthenticationCompletesWithoutResubmission()
    {
        var (_, fake) = Native();
        var ld = (ILDPlayerService)(object)fake;
        fake.UiSequence.Enqueue(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowLogin",
                    "ld74-facebook-581-login.xml"
                )
            )
        );
        fake.UiSequence.Enqueue(Own());
        var result = await new PFlowNativeOtpChallengeAdapter(
            ld,
            _directory,
            Binding()
        ).ObserveAsync(Binding(), default);
        Assert.Equal(PFlowOtpChallengeState.NotRequired, result.State);
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, fake.Inputs);
        Assert.Equal(0, fake.LoginSubmissions);
    }

    [Fact]
    public async Task NumericLinkBelowFoldIsScrolledIntoViewBeforeVerifyingUid()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.HideLinkUntilScroll = true;
        var result = await nav.ObserveAsync(Binding(), default);
        Assert.True(PFlowAutomaticSessionService.Verified(result, Binding()));
        Assert.Contains("SwipeAsync", fake.Calls);
        Assert.Equal(Own(), fake.Ui);
    }

    [Fact]
    public async Task LogoutThenPasswordLandingUsesOnlyBoundDevice()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        await nav.OpenLoginAsync(Binding("456"), true, default);
        Assert.Equal(3, fake.Taps);
        Assert.Equal(PFlowLoginScreenKind.LoginRequired, PFlowLoginScreen.Classify(fake.Ui));
        Assert.All(fake.Indices, index => Assert.Equal(Index, index));
        Assert.DoesNotContain("InputTextAdbKeyboardAsync", fake.Calls);
    }

    [Fact]
    public async Task NoRotationPermissionDoesNotLogOut()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.OpenLoginAsync(Binding("456"), false, default)
        );
        Assert.Equal(0, fake.Taps);
        Assert.DoesNotContain("OpenFacebookMenuFromFeedAsync", fake.Calls);
    }

    [Fact]
    public async Task ExplicitLogoutCheckDoesNotReuseTheSameAccount()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        await nav.LogoutAndOpenLoginAsync(Binding(), default);
        Assert.Equal(PFlowLoginScreenKind.LoginRequired, PFlowLoginScreen.Classify(fake.Ui));
        Assert.Contains("OpenFacebookMenuFromFeedAsync", fake.Calls);
        Assert.Equal(2, fake.Taps);
    }

    [Fact]
    public async Task ExplicitLogoutWorksFromCurrentPageWithoutPersonalProfileNavigation()
    {
        var (nav, fake) = Native();
        fake.Ui = Xml(Node("Manage Page"), Node("Fixture Page"));
        await nav.LogoutAndOpenLoginAsync(Binding(), default);
        Assert.Equal(2, fake.Taps);
        Assert.Equal(0, fake.Backs);
        Assert.Equal(PFlowLoginScreenKind.LoginRequired, PFlowLoginScreen.Classify(fake.Ui));
    }

    [Fact]
    public void TextOnlyUppercaseSaveInfoButtonsUseTheirObservedBounds()
    {
        Assert.Equal(PFlowLoginScreenKind.SaveLoginInfo, PFlowLoginScreen.Classify(SaveInfo()));
        Assert.Equal((403, 793), PFlowNativeLoginNavigator.Control(SaveInfo(), "Not now"));
        Assert.Equal((403, 793), PFlowNativeAccountNavigator.FindControl(SaveInfo(), "Not now"));
        Assert.False(PFlowNativeAccountNavigator.MenuSurface(SaveInfo()));
    }

    [Fact]
    public async Task LogoutDismissesSaveInfoThenConfirmsAndOpensFullPasswordForm()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        fake.IncludeLogoutPrompts = true;
        await nav.LogoutAndOpenLoginAsync(Binding(), default);
        Assert.Equal(4, fake.Taps);
        Assert.Equal(1, fake.MenuLogouts);
        Assert.Equal(1, fake.SaveInfoDismissals);
        Assert.Equal(1, fake.LogoutConfirmations);
        Assert.Equal(0, fake.LoginSubmissions);
        Assert.Equal(PFlowLoginScreenKind.LoginRequired, PFlowLoginScreen.Classify(fake.Ui));
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 2)]
    public async Task ResumingPendingLogoutDoesNotTapTheMenuAgain(bool saveInfo, int taps)
    {
        var (nav, fake) = Native();
        fake.Ui = saveInfo ? SaveInfo() : LogoutConfirm();
        await nav.LogoutAndOpenLoginAsync(Binding(), default);
        Assert.Equal(taps, fake.Taps);
        Assert.DoesNotContain("OpenFacebookMenuFromFeedAsync", fake.Calls);
        Assert.Equal(0, fake.MenuLogouts);
        Assert.Equal(1, fake.LogoutConfirmations);
        Assert.Equal(PFlowLoginScreenKind.LoginRequired, PFlowLoginScreen.Classify(fake.Ui));
    }

    [Fact]
    public async Task PendingLogoutCannotBeConfirmedWithoutRotationPermission()
    {
        var (nav, fake) = Native();
        fake.Ui = SaveInfo();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nav.OpenLoginAsync(Binding(), false, default)
        );
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task SavedLandingUsesAnotherProfileWithoutSelectingContinue()
    {
        var (nav, fake) = Native();
        fake.Ui = SavedLanding();
        Assert.True(PFlowNativeAccountNavigator.LoginLanding(fake.Ui));
        await nav.OpenLoginAsync(Binding(), false, default);
        Assert.Equal(1, fake.Taps);
        Assert.DoesNotContain("OpenFacebookMenuFromFeedAsync", fake.Calls);
        Assert.Equal(PFlowLoginScreenKind.LoginRequired, PFlowLoginScreen.Classify(fake.Ui));
        Assert.False(
            PFlowNativeAccountNavigator.LoginLanding(Xml(Node("Use another profile", true)))
        );
    }

    [Theory]
    [InlineData("[20,1140][700,1280]", 1160)]
    [InlineData("[20,1175][700,1280]", null)]
    public void LogoutAvoidsTheFixedBottomNavigation(string bounds, int? expectedY)
    {
        var xml = Xml(
            Node("", bounds: "[0,0][720,1280]"),
            Node("Log out", true, bounds),
            Node("Home, tab 1 of 6", true, "[0,1180][120,1280]"),
            Node("Friends, tab 2 of 6", true, "[120,1180][240,1280]")
        );
        Assert.Equal(expectedY, PFlowNativeAccountNavigator.FindControl(xml, "Log out")?.Y);
    }

    [Fact]
    public void SelectedMenuTabAnchorsMenuAfterHeaderHasScrolledOffscreen()
    {
        var tab = Node("Menu, tab 6 of 6", true, "[600,1180][720,1280]");
        tab.SetAttributeValue("selected", "true");
        Assert.True(PFlowNativeAccountNavigator.MenuSurface(Xml(tab, Node("Meta Quest 3S"))));
        tab.SetAttributeValue("selected", "false");
        Assert.False(PFlowNativeAccountNavigator.MenuSurface(Xml(tab, Node("Meta Quest 3S"))));
    }

    [Fact]
    public void ProfessionalPersonalProfileAndCapitalizedSettingsAreSupported()
    {
        Assert.True(
            PFlowNativeAccountNavigator.OwnProfile(
                Xml(
                    Node("Edit cover photo button", true),
                    Node("Edit profile picture", true),
                    Node("More profile settings", true)
                )
            )
        );
        Assert.Equal(
            "123",
            PFlowNativeAccountNavigator.ReadOwnProfileUid(
                Settings().Replace("Profile settings", "Profile Settings")
            )
        );
    }

    [Fact]
    public async Task NormalSameAccountNavigationStillReusesSession()
    {
        var (nav, fake) = Native();
        fake.Ui = Own();
        await nav.OpenLoginAsync(Binding(), true, default);
        Assert.Equal(Own(), fake.Ui);
        Assert.DoesNotContain("OpenFacebookMenuFromFeedAsync", fake.Calls);
    }

    [Fact]
    public void ObservedMoreProfileSettingsControlUsesFreshBounds() =>
        Assert.NotNull(
            PFlowNativeAccountNavigator.FindControl(
                Xml(Node("More profile settings", true)),
                "More profile settings"
            )
        );

    [Fact]
    public void PageSettingsNeedsItsExplicitSwitcherControl()
    {
        Assert.True(
            PFlowNativeAccountNavigator.PageSettings(
                Xml(
                    Node("Page Settings"),
                    Node("Profile switcher, Double tap to open a list of your profiles", true)
                )
            )
        );
        Assert.False(PFlowNativeAccountNavigator.PageSettings(Xml(Node("Page Settings"))));
        Assert.Null(
            PFlowNativeAccountNavigator.ReadOwnProfileUid(
                Xml(
                    Node("Page Settings"),
                    Node("Your profile link"),
                    Node("https://www.facebook.com/profile.php?id=123")
                )
            )
        );
    }

    [Fact]
    public void SavedLoginLandingIsRecognizedWithoutClickingAnAccountName() =>
        Assert.True(
            PFlowNativeAccountNavigator.LoginLanding(Xml(Node("Log into another account", true)))
        );

    [Fact]
    public async Task PasswordLoginSubmitsOnceThenVerifiesOwnNumericUid()
    {
        var (nav, fake) = Native();
        fake.Ui = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLogin",
                "ld74-facebook-581-login.xml"
            )
        );
        using var credential = PFlowCredentialImport.Parse(
            "123|fixture-password||",
            Binding(),
            "fixture@example.invalid",
            PFlowTwoFactorKind.None
        );
        var observed = await nav.LoginAsync(
            Binding(),
            credential,
            _ => Task.CompletedTask,
            default
        );
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding()));
        Assert.Equal(1, fake.LoginSubmissions);
        Assert.Equal(2, fake.Inputs);
        Assert.All(fake.Indices, i => Assert.Equal(Index, i));
    }

    [Fact]
    public async Task FreshDeviceInstallsAndVerifiesKeyboardBeforeEnablingInput()
    {
        var (_, fake) = Native();
        fake.KeyboardInstalled = false;
        await new PFlowSessionPreparation(
            (ILDPlayerService)(object)fake,
            _directory
        ).EnsureTextInputAsync(Binding(), _ => Task.CompletedTask, default);
        Assert.True(fake.KeyboardInstalled);
        Assert.True(fake.KeyboardActive);
        Assert.True(
            fake.Calls.IndexOf("InstallAdbKeyboardAsync")
                < fake.Calls.IndexOf("EnableAdbKeyboardAsync")
        );
        Assert.Equal(2, fake.Calls.Count(c => c == "IsAdbKeyboardInstalledAsync"));
        Assert.Equal(0, fake.Inputs);
        Assert.All(fake.Indices, i => Assert.Equal(Index, i));
    }

    [Fact]
    public async Task ExistingKeyboardIsVerifiedWithoutReinstalling()
    {
        var (_, fake) = Native();
        await new PFlowSessionPreparation(
            (ILDPlayerService)(object)fake,
            _directory
        ).EnsureTextInputAsync(Binding(), _ => Task.CompletedTask, default);
        Assert.DoesNotContain("InstallAdbKeyboardAsync", fake.Calls);
        Assert.Contains("IsAdbKeyboardActiveAsync", fake.Calls);
    }

    [Theory]
    [InlineData("install-failed")]
    [InlineData("package-still-missing")]
    [InlineData("enable-failed")]
    [InlineData("wrong-active-ime")]
    public async Task KeyboardSetupFailureStopsBeforeAnyCredentialInput(string failure)
    {
        var (_, fake) = Native();
        fake.KeyboardInstalled = false;
        fake.KeyboardInstallSucceeds = failure != "install-failed";
        fake.PackageAppearsAfterInstall = failure != "package-still-missing";
        fake.KeyboardEnableSucceeds = failure != "enable-failed";
        fake.KeyboardActivationSucceeds = failure != "wrong-active-ime";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowSessionPreparation(
                (ILDPlayerService)(object)fake,
                _directory
            ).EnsureTextInputAsync(Binding(), _ => Task.CompletedTask, default)
        );
        Assert.Equal(0, fake.Inputs);
        Assert.Equal(0, fake.LoginSubmissions);
        if (failure is "install-failed" or "package-still-missing")
            Assert.DoesNotContain("EnableAdbKeyboardAsync", fake.Calls);
    }

    [Fact]
    public async Task WrongDeviceBindingCannotInstallKeyboard()
    {
        var (_, fake) = Native();
        fake.KeyboardInstalled = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowSessionPreparation(
                (ILDPlayerService)(object)fake,
                _directory
            ).EnsureTextInputAsync(
                Binding() with
                {
                    InstanceIdentity = "replacement",
                },
                _ => Task.CompletedTask,
                default
            )
        );
        Assert.DoesNotContain("InstallAdbKeyboardAsync", fake.Calls);
    }

    [Fact]
    public void ScrolledMenuRequiresItsAnchoredDrawerControls()
    {
        Assert.True(
            PFlowNativeAccountNavigator.MenuSurface(
                Xml(Node("Menu"), Node("Close menu", true), Node("Log out", true))
            )
        );
        Assert.False(
            PFlowNativeAccountNavigator.MenuSurface(Xml(Node("Menu"), Node("Log out", true)))
        );
    }

    public class Device : DispatchProxy
    {
        public string Ui = "",
            Link = "https://www.facebook.com/profile.php?id=123";
        public string Foreground = "com.facebook.katana/ProfileActivity";
        public int Taps,
            Backs;
        public List<int> Indices = [];
        public List<string> Calls = [];
        public int LoginSubmissions,
            Inputs;
        public bool IncludeLogoutPrompts;
        public bool KeyboardInstalled = true,
            KeyboardActive = true,
            KeyboardInstallSucceeds = true,
            PackageAppearsAfterInstall = true,
            KeyboardEnableSucceeds = true,
            KeyboardActivationSucceeds = true;
        public bool HideLinkUntilScroll;
        public bool EmbeddedProfile;
        public bool SettingsHasBack;
        public bool UseCopySettings;
        public bool CopyStartsClipped;
        public bool ClipboardResetWorks = true,
            CopyUpdatesClipboard = true;
        public string? ProfileClipboard,
            CopiedProfileLink;
        public int CopyTaps;
        public string? RecoveredUid;
        public string? UidAfterRelaunch;
        public bool RecentAppsVerifiedEmpty = true;
        public bool RelaunchSucceeds = true;
        public int UidRecoveryStops,
            UidRecoveryLaunches;
        public int MissingInstanceReads,
            InstanceReads;
        public int PersonalRecoveries;
        public Queue<string> UiSequence = new(),
            ForegroundSequence = new();
        public int MenuLogouts,
            SaveInfoDismissals,
            LogoutConfirmations;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add(method!.Name);
            if (args?.FirstOrDefault() is int index)
                Indices.Add(index);
            switch (method.Name)
            {
                case "GetInstancesAsync":
                    InstanceReads++;
                    if (MissingInstanceReads-- > 0)
                        return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([]);
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                        new()
                        {
                            Index = Index,
                            Name = "fixture-LD",
                            IsAndroidStarted = true,
                        },
                    ]);
                case "GetCurrentFocusedActivityAsync":
                    if (ForegroundSequence.Count > 0)
                        Foreground = ForegroundSequence.Dequeue();
                    return Task.FromResult<string?>(Foreground);
                case "DumpUiHierarchyAsync":
                    if (UiSequence.Count > 0)
                        Ui = UiSequence.Dequeue();
                    return Task.FromResult<string?>(Ui);
                case "PrepareProfileLinkClipboardAsync":
                    if (ClipboardResetWorks)
                        ProfileClipboard = "pflow-profile-proof-fixture";
                    return Task.FromResult(ClipboardResetWorks);
                case "ReadCopiedProfileLinkAsync":
                    return Task.FromResult(ProfileClipboard);
                case "EnsureSwitchedToPersonalProfileAsync":
                    PersonalRecoveries++;
                    Assert.Equal("Fixture Personal", args![1]);
                    if (RecoveredUid == null)
                        return Task.FromResult(false);
                    Link = "https://www.facebook.com/profile.php?id=" + RecoveredUid;
                    Ui = Own();
                    return Task.FromResult(true);
                case "SwipeAsync":
                    if (UseCopySettings && CopyStartsClipped)
                    {
                        CopyStartsClipped = false;
                        Ui = ProfessionalSettings();
                        return Task.FromResult(true);
                    }
                    if (!EmbeddedProfile)
                        Ui = Settings(Link);
                    return Task.FromResult(true);
                case "OpenDeepLinkAsync":
                    Assert.Equal("fb://profile/123", args![1]);
                    Ui = Own();
                    return Task.FromResult(true);
                case "OpenFacebookMenuFromFeedAsync":
                    Ui = Xml(Node("Menu"), Node("Your shortcuts"), Node("Log out", true));
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
                    if (Ui == ProfessionalSettings())
                    {
                        Assert.Equal(
                            PFlowNativeAccountNavigator.CopyProfileLinkControl(Ui),
                            ((int)args![1]!, (int)args[2]!)
                        );
                        CopyTaps++;
                        if (CopyUpdatesClipboard)
                            ProfileClipboard = CopiedProfileLink;
                        return Task.FromResult(true);
                    }
                    if (Foreground == PFlowAndroidCredentialPrompt.Activity)
                    {
                        Assert.Equal(
                            PFlowAndroidCredentialPrompt.CloseControl(Ui),
                            ((int)args![1]!, (int)args[2]!)
                        );
                        Foreground = "com.facebook.katana/ProfileActivity";
                        Ui = Own();
                        return Task.FromResult(true);
                    }
                    if (PFlowLoginScreen.Classify(Ui) == PFlowLoginScreenKind.SaveLoginInfo)
                    {
                        Assert.Equal(
                            PFlowNativeAccountNavigator.FindControl(Ui, "Not now"),
                            ((int)args![1]!, (int)args[2]!)
                        );
                        SaveInfoDismissals++;
                        Ui = LogoutConfirm();
                        return Task.FromResult(true);
                    }
                    if (Ui.Contains("Log out of your account?"))
                    {
                        Assert.Equal(
                            PFlowNativeAccountNavigator.FindControl(Ui, "Log out"),
                            ((int)args![1]!, (int)args[2]!)
                        );
                        LogoutConfirmations++;
                        Ui = SavedLanding();
                        return Task.FromResult(true);
                    }
                    if (Ui == SavedLanding())
                        Assert.Equal(
                            PFlowNativeAccountNavigator.FindControl(Ui, "Use another profile"),
                            ((int)args![1]!, (int)args[2]!)
                        );
                    if (PFlowLoginScreen.Classify(Ui) == PFlowLoginScreenKind.LoginRequired)
                    {
                        var point = ((int)args![1]!, (int)args[2]!);
                        if (PFlowNativeLoginNavigator.Control(Ui, "Log in") == point)
                        {
                            LoginSubmissions++;
                            Ui = Own();
                        }
                        else
                        {
                            var doc = XDocument.Parse(Ui);
                            var role =
                                PFlowNativeLoginNavigator.Control(Ui, "login") == point
                                    ? "login"
                                    : "password";
                            foreach (
                                var field in doc.Descendants("node")
                                    .Where(n =>
                                        (string?)n.Attribute("class") == "android.widget.EditText"
                                    )
                            )
                                field.SetAttributeValue(
                                    "focused",
                                    ((string?)field.Attribute("password") == "true")
                                    == (role == "password")
                                        ? "true"
                                        : "false"
                                );
                            Ui = doc.ToString();
                        }
                        return Task.FromResult(true);
                    }
                    if (PFlowNativeAccountNavigator.OwnProfile(Ui))
                        Ui =
                            HideLinkUntilScroll
                                ? Xml(
                                    Node("", bounds: "[0,0][720,1280]"),
                                    Node("Profile Settings"),
                                    Node("Back", true),
                                    Node("Your profile link")
                                )
                            : UseCopySettings ? ProfessionalSettings(CopyStartsClipped)
                            : SettingsHasBack
                                ? Xml(
                                    Node("Profile settings"),
                                    Node("Back", true),
                                    Node("Your profile link"),
                                    Node(Link)
                                )
                            : Settings(Link);
                    else if (Ui.Contains("Your shortcuts"))
                    {
                        MenuLogouts++;
                        Ui = IncludeLogoutPrompts
                            ? SaveInfo()
                            : Xml(Node("Log into another account", true));
                    }
                    else
                        Ui = File.ReadAllText(
                            Path.Combine(
                                AppContext.BaseDirectory,
                                "Fixtures",
                                "PFlowLogin",
                                "ld74-facebook-581-login.xml"
                            )
                        );
                    return Task.FromResult(true);
                case "SendKeyAsync":
                    Backs++;
                    Ui = Own();
                    return Task.FromResult(true);
                case "ExecuteAdbAsync":
                    if (((string)args![1]!).Contains("dumpsys activity -a recents"))
                        return Task.FromResult(
                            RecentAppsVerifiedEmpty
                                ? "ACTIVITY MANAGER RECENT TASKS (dumpsys activity recents)\n  mRecentsUid=-1\n  mRecentsComponent=null\n"
                                : "Unsupported recents state"
                        );
                    return Task.FromResult(
                        "Active default network: 100\nCurrent Networks:\n NetworkAgentInfo{network{100} ni{[state: CONNECTED/CONNECTED]} nc{[Capabilities: INTERNET&VALIDATED]}}\n"
                    );
                case "WaitForBootCompletedAsync":
                case "EnsureAtxAgentRunningAsync":
                    return Task.FromResult(true);
                case "StopFacebookAppAsync":
                    UidRecoveryStops++;
                    return Task.FromResult(true);
                case "LaunchAppAsync":
                    UidRecoveryLaunches++;
                    if (UidAfterRelaunch != null)
                        Link = "https://www.facebook.com/profile.php?id=" + UidAfterRelaunch;
                    return Task.FromResult(RelaunchSucceeds);
                case "IsAdbKeyboardInstalledAsync":
                    return Task.FromResult(KeyboardInstalled);
                case "InstallAdbKeyboardAsync":
                    KeyboardInstalled = KeyboardInstallSucceeds && PackageAppearsAfterInstall;
                    return Task.FromResult(KeyboardInstallSucceeds);
                case "EnableAdbKeyboardAsync":
                    KeyboardActive = KeyboardEnableSucceeds && KeyboardActivationSucceeds;
                    return Task.FromResult(KeyboardEnableSucceeds);
                case "IsAdbKeyboardActiveAsync":
                    return Task.FromResult(KeyboardActive);
                case "InputTextAdbKeyboardAsync":
                    Inputs++;
                    var inputDoc = XDocument.Parse(Ui);
                    inputDoc
                        .Descendants("node")
                        .Single(n =>
                            (string?)n.Attribute("class") == "android.widget.EditText"
                            && (string?)n.Attribute("focused") == "true"
                        )
                        .SetAttributeValue("text", (string)args![1]!);
                    Ui = inputDoc.ToString();
                    return Task.FromResult(true);
                default:
                    throw new InvalidOperationException(
                        "Unexpected fixture operation: " + method.Name
                    );
            }
        }
    }
}
