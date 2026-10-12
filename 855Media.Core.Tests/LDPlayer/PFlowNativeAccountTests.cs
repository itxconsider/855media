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
        new XElement("hierarchy", nodes).ToString();

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

    private (PFlowNativeAccountNavigator Navigator, Device Fake) Native()
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
                }
            ),
            fake
        );
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
        Assert.Equal(1, fake.PersonalRecoveries);
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
        public bool SettingsHasBack;
        public string? RecoveredUid;
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
                case "EnsureSwitchedToPersonalProfileAsync":
                    PersonalRecoveries++;
                    Assert.Equal("Fixture Personal", args![1]);
                    if (RecoveredUid == null)
                        return Task.FromResult(false);
                    Link = "https://www.facebook.com/profile.php?id=" + RecoveredUid;
                    Ui = Own();
                    return Task.FromResult(true);
                case "SwipeAsync":
                    Ui = Settings(Link);
                    return Task.FromResult(true);
                case "OpenFacebookMenuFromFeedAsync":
                    Ui = Xml(Node("Menu"), Node("Your shortcuts"), Node("Log out", true));
                    return Task.FromResult(true);
                case "TapAsync":
                    Taps++;
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
                    return Task.FromResult(
                        "Active default network: 100\nCurrent Networks:\n NetworkAgentInfo{network{100} ni{[state: CONNECTED/CONNECTED]} nc{[Capabilities: INTERNET&VALIDATED]}}\n"
                    );
                case "WaitForBootCompletedAsync":
                case "EnsureAtxAgentRunningAsync":
                case "LaunchAppAsync":
                    return Task.FromResult(true);
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
