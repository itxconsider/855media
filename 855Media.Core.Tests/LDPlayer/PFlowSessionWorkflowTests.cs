using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowSessionWorkflowTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "pflow-session-" + Guid.NewGuid().ToString("N")
    );

    private PFlowAccountBinding Binding(
        string uid = "123",
        int index = 900075,
        string package = "com.facebook.katana"
    ) => new(PFlowDeviceRouting.ReadIdentity(directory, 900075), index, "fixture-LD", package, uid);

    public PFlowSessionWorkflowTests()
    {
        Directory.CreateDirectory(Path.Combine(directory, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(directory, "vms", "leidian900075"));
        File.WriteAllText(
            Path.Combine(directory, "vms", "config", "leidian900075.config"),
            "{\"propertySettings.phoneAndroidId\":\"fixture-only-android-id\"}"
        );
        File.WriteAllText(Path.Combine(directory, "vms", "leidian900075", "data.vmdk"), "fixture");
    }

    public void Dispose() => Directory.Delete(directory, true);

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLogin",
                "ld74-facebook-581-" + name + ".xml"
            )
        );

    [Theory]
    [InlineData("login", PFlowLoginScreenKind.LoginRequired)]
    [InlineData("wrong-credentials", PFlowLoginScreenKind.CredentialsRejected)]
    [InlineData("approval", PFlowLoginScreenKind.DeviceApprovalRequired)]
    [InlineData("confirmation-methods", PFlowLoginScreenKind.VerificationMethods)]
    [InlineData("authenticator", PFlowLoginScreenKind.AuthenticatorCodeRequired)]
    [InlineData("code-accepted", PFlowLoginScreenKind.SaveLoginInfo)]
    [InlineData("own-profile", PFlowLoginScreenKind.OwnPersonalProfile)]
    [InlineData("human-checkpoint", PFlowLoginScreenKind.Checkpoint)]
    public void ClassifiesInstalledFixturesWithoutClaimingUid(
        string name,
        PFlowLoginScreenKind expected
    )
    {
        Assert.Equal(expected, PFlowLoginScreen.Classify(Fixture(name)));
        Assert.Equal(
            expected != PFlowLoginScreenKind.OwnPersonalProfile,
            PFlowLoginScreen.RequiresAuthentication(expected)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("<hierarchy />")]
    public void UnknownLayoutsRemainUnsupported(string? xml) =>
        Assert.Equal(PFlowLoginScreenKind.Unsupported, PFlowLoginScreen.Classify(xml));

    [Fact]
    public void DifferentPackageCannotSupplyChallenge() =>
        Assert.Equal(
            PFlowLoginScreenKind.Unsupported,
            PFlowLoginScreen.Classify(Fixture("authenticator"), "com.other.app")
        );

    [Fact]
    public void AmbiguousOrDisabledCodeFieldIsUnsupported()
    {
        var doc = XDocument.Parse(Fixture("authenticator"));
        var field = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("class") == "android.widget.EditText");
        field.AddAfterSelf(new XElement(field));
        Assert.Equal(PFlowLoginScreenKind.Unsupported, PFlowLoginScreen.Classify(doc.ToString()));
        field.ElementsAfterSelf("node").First().Remove();
        field.SetAttributeValue("enabled", "false");
        Assert.Equal(PFlowLoginScreenKind.Unsupported, PFlowLoginScreen.Classify(doc.ToString()));
    }

    [Fact]
    public void CodeMustMatchExactlyAndCannotBeDisposed()
    {
        var doc = XDocument.Parse(Fixture("authenticator"));
        var field = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("class") == "android.widget.EditText");
        using var code = PFlowOtpCode.Received("001234", DateTimeOffset.UtcNow);
        field.SetAttributeValue("text", "001234");
        Assert.True(PFlowNativeOtpChallengeAdapter.CodeMatches(doc.ToString(), code));
        field.SetAttributeValue("text", "1234");
        Assert.False(PFlowNativeOtpChallengeAdapter.CodeMatches(doc.ToString(), code));
        field.SetAttributeValue("text", "001234");
        code.Dispose();
        Assert.False(PFlowNativeOtpChallengeAdapter.CodeMatches(doc.ToString(), code));
    }

    [Fact]
    public void SelectionUsesAssignedInstancePackageAndExplicitActionChoice()
    {
        PFlowPersonalAccount Account(string uid, int index, string package, bool selected = true) =>
            new() { Binding = Binding(uid, index, package), SelectedForScreenTime = selected };
        var accounts = new[]
        {
            Account("123", 900075, "com.facebook.katana"),
            Account("456", 900075, "com.facebook.katana"),
            Account("789", 900076, "com.facebook.katana"),
            Account("111", 900075, "com.other.app"),
            Account("222", 900075, "com.facebook.katana", false),
        };
        Assert.Equal(
            new[] { "123", "456" },
            PFlowAccountSelection
                .ForScreenTime(accounts, 900075, "com.facebook.katana")
                .Select(a => a.Binding.OwnerUid)
        );
    }

    [Fact]
    public void DuplicateAccountMappingsAreRejected()
    {
        var account = new PFlowPersonalAccount
        {
            Binding = Binding(),
            SelectedForScreenTime = true,
        };
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountSelection.ForScreenTime(
                new[] { account, account },
                900075,
                "com.facebook.katana"
            )
        );
    }

    private (ILDPlayerService Service, Device Fake) DeviceProxy()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    [Fact]
    public void MultipleMappingsCannotBypassGuardBySelectingOnlyOneOrHidingProfileRow()
    {
        var accounts = new[]
        {
            new PFlowPersonalAccount { Binding = Binding("123"), SelectedForScreenTime = true },
            new PFlowPersonalAccount { Binding = Binding("456"), SelectedForScreenTime = false },
        };
        Assert.Throws<NotSupportedException>(() =>
            PFlowAccountSelection.RequireSupportedSingleLogin(
                accounts,
                900075,
                "com.facebook.katana",
                new[] { "123" }
            )
        );
        Assert.Throws<NotSupportedException>(() =>
            PFlowAccountSelection.RequireSupportedSingleLogin(
                accounts.Take(1),
                900075,
                "com.facebook.katana",
                new[] { "123", "456" }
            )
        );
        PFlowAccountSelection.RequireSupportedSingleLogin(
            accounts.Take(1),
            900075,
            "com.facebook.katana",
            new[] { "123" }
        );
    }

    [Fact]
    public void LoginSubmissionRequiresMatchingIdentifierAndPasswordDraft()
    {
        using var credential = PFlowCredentialImport.Parse(
            "123|fixture-password||",
            Binding(),
            "fixture@example.invalid",
            PFlowTwoFactorKind.None
        );
        var doc = XDocument.Parse(Fixture("login"));
        var fields = doc.Descendants("node")
            .Where(n => (string?)n.Attribute("class") == "android.widget.EditText")
            .ToArray();
        var login = fields.Single(n => (string?)n.Attribute("password") != "true");
        var password = fields.Single(n => (string?)n.Attribute("password") == "true");
        Assert.False(PFlowNativeLoginNavigator.CanSubmitLogin(doc.ToString(), credential));
        login.SetAttributeValue("text", "fixture@example.invalid");
        password.SetAttributeValue("text", "fixture-password");
        Assert.True(PFlowNativeLoginNavigator.CanSubmitLogin(doc.ToString(), credential));
        login.SetAttributeValue("text", "other@example.invalid");
        Assert.False(PFlowNativeLoginNavigator.CanSubmitLogin(doc.ToString(), credential));
        login.SetAttributeValue("text", "fixture@example.invalid");
        password.SetAttributeValue("text", new string('•', "fixture-password".Length));
        Assert.True(PFlowNativeLoginNavigator.CanSubmitLogin(doc.ToString(), credential));
        password.SetAttributeValue("text", "wrong-password");
        Assert.False(PFlowNativeLoginNavigator.CanSubmitLogin(doc.ToString(), credential));
        credential.Dispose();
        Assert.False(PFlowNativeLoginNavigator.CanSubmitLogin(doc.ToString(), credential));
    }

    [Fact]
    public void DiagnosticsHideAuthenticationValuesAndSuppressInputScreenshots()
    {
        var doc = XDocument.Parse(Fixture("authenticator"));
        var field = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("class") == "android.widget.EditText");
        field.SetAttributeValue("text", "001234");
        field.SetAttributeValue("content-desc", "Code, 001234");
        var parent = field.Parent!;
        parent.SetAttributeValue("content-desc", "Code, 001234");
        Assert.True(PFlowUiPrivacy.ContainsInput(doc.ToString()));
        Assert.DoesNotContain("001234", PFlowUiPrivacy.RedactForDiagnostics(doc.ToString()));
        Assert.True(PFlowUiPrivacy.ContainsInput("malformed"));
        Assert.False(PFlowUiPrivacy.ContainsInput(Fixture("own-profile")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationPreservesExistingSessionAndOrdersInternetBeforeApp(bool running)
    {
        var (ld, fake) = DeviceProxy();
        fake.Running = running;
        await new PFlowSessionPreparation(ld, directory).PrepareAsync(
            Binding(),
            _ => Task.CompletedTask,
            default
        );
        Assert.Equal(running ? 0 : 1, fake.Calls.Count(c => c == "LaunchInstanceAsync"));
        Assert.True(fake.Calls.IndexOf("ExecuteAdbAsync") < fake.Calls.IndexOf("LaunchAppAsync"));
        Assert.DoesNotContain("ClearFacebookCacheAsync", fake.Calls);
        Assert.DoesNotContain("StopFacebookAppAsync", fake.Calls);
        Assert.All(fake.Indices, i => Assert.Equal(900075, i));
    }

    [Theory]
    [InlineData("mCurrentFocus=Window{fixture com.ldmnq.launcher3/Launcher}", true)]
    [InlineData("mCurrentFocus=Window{fixture com.android.launcher3/Launcher}", true)]
    [InlineData("mCurrentFocus=Window{fixture com.facebook.katana/ProfileActivity}", false)]
    [InlineData("mCurrentFocus=Window{fixture com.android.chrome/Main}", false)]
    public async Task NextPostRestoresOnlyTheAssignedFacebookAppFromAndroidHome(
        string focus,
        bool launches
    )
    {
        var (ld, fake) = DeviceProxy();
        fake.FocusedActivity = focus;
        await new PFlowSessionPreparation(ld, directory).RestorePostForegroundFromLauncherAsync(
            Binding(),
            default
        );
        Assert.Equal(launches ? 1 : 0, fake.Calls.Count(c => c == "LaunchAppAsync"));
        Assert.DoesNotContain("StopFacebookAppAsync", fake.Calls);
        Assert.DoesNotContain("ClearFacebookCacheAsync", fake.Calls);
        Assert.All(fake.Indices, index => Assert.Equal(900075, index));
    }

    [Fact]
    public async Task BindingMismatchStopsBeforeLaunchingAnything()
    {
        var (ld, fake) = DeviceProxy();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowSessionPreparation(ld, directory).PrepareAsync(
                Binding() with
                {
                    InstanceIdentity = "wrong",
                },
                _ => Task.CompletedTask,
                default
            )
        );
        Assert.DoesNotContain("LaunchAppAsync", fake.Calls);
        Assert.DoesNotContain("LaunchInstanceAsync", fake.Calls);
    }

    [Fact]
    public async Task CancellationWhileWaitingForInternetDoesNotOpenApp()
    {
        var (ld, fake) = DeviceProxy();
        using var cancellation = new CancellationTokenSource();
        fake.CancelOnInternet = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowSessionPreparation(ld, directory).PrepareAsync(
                Binding(),
                _ => Task.CompletedTask,
                cancellation.Token
            )
        );
        Assert.DoesNotContain("LaunchAppAsync", fake.Calls);
    }

    [Fact]
    public async Task InstalledAdapterAcceptsOneCodeAndRefusesDuplicateSubmission()
    {
        var (ld, fake) = DeviceProxy();
        fake.Ui = Fixture("authenticator");
        fake.AfterSubmit = Fixture("code-accepted");
        var adapter = new PFlowNativeOtpChallengeAdapter(ld, directory, Binding());
        var challenge = await adapter.ObserveAsync(Binding(), default);
        using var code = PFlowOtpCode.Received("001234", DateTimeOffset.UtcNow);
        Assert.Equal(
            PFlowOtpSubmission.Confirmed,
            await adapter.SubmitAsync(challenge.Request!, code, default)
        );
        Assert.Equal(1, fake.Submissions);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.SubmitAsync(challenge.Request!, code, default)
        );
        Assert.Equal(1, fake.Submissions);
    }

    [Fact]
    public async Task InstalledAdapterStopsWhenAccountContextChanges()
    {
        var (ld, fake) = DeviceProxy();
        fake.Ui = Fixture("authenticator");
        var adapter = new PFlowNativeOtpChallengeAdapter(ld, directory, Binding());
        var challenge = await adapter.ObserveAsync(Binding(), default);
        fake.Ui = fake.Ui.Replace("Leon Gallagher", "Different account", StringComparison.Ordinal);
        using var code = PFlowOtpCode.Received("001234", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.SubmitAsync(challenge.Request!, code, default)
        );
        Assert.DoesNotContain("InputTextAdbKeyboardAsync", fake.Calls);
        Assert.Equal(0, fake.Submissions);
    }

    [Fact]
    public async Task InstalledAdapterRejectsOtherBindingAndCancelledObservation()
    {
        var (ld, fake) = DeviceProxy();
        var adapter = new PFlowNativeOtpChallengeAdapter(ld, directory, Binding());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ObserveAsync(Binding("456"), default)
        );
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.ObserveAsync(Binding(), cancellation.Token)
        );
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task CheckpointAfterCodeIsUnknownAndNeverRetried()
    {
        var (ld, fake) = DeviceProxy();
        fake.Ui = Fixture("authenticator");
        fake.AfterSubmit = Fixture("human-checkpoint");
        var adapter = new PFlowNativeOtpChallengeAdapter(ld, directory, Binding());
        var challenge = await adapter.ObserveAsync(Binding(), default);
        using var code = PFlowOtpCode.Received("001234", DateTimeOffset.UtcNow);
        Assert.Equal(
            PFlowOtpSubmission.Unknown,
            await adapter.SubmitAsync(challenge.Request!, code, default)
        );
        Assert.Equal(1, fake.Submissions);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.SubmitAsync(challenge.Request!, code, default)
        );
        Assert.Equal(1, fake.Submissions);
    }

    [Fact]
    public async Task AndroidNetworkChangeStopsOtpBeforeInputOrSubmit()
    {
        var (ld, fake) = DeviceProxy();
        fake.Ui = Fixture("authenticator");
        var adapter = new PFlowNativeOtpChallengeAdapter(ld, directory, Binding());
        var challenge = await adapter.ObserveAsync(Binding(), default);
        fake.Network = 101;
        using var code = PFlowOtpCode.Received("001234", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.SubmitAsync(challenge.Request!, code, default)
        );
        Assert.DoesNotContain("InputTextAdbKeyboardAsync", fake.Calls);
        Assert.Equal(0, fake.Submissions);
    }

    [Fact]
    public async Task VerifiedExistingPersonalSessionIsReusedWithoutSwitching()
    {
        var account = new PFlowPersonalAccount { Binding = Binding() };
        var navigator = new SessionNavigator();
        var result = await new PFlowAccountSessionService(navigator).PreparePersonalAsync(
            account,
            default,
            true
        );
        Assert.True(result.IsReady);
        Assert.Equal(account.Binding, result.Binding);
        Assert.False(string.IsNullOrWhiteSpace(result.AttemptId));
        Assert.Equal(0, navigator.Switches);
    }

    [Fact]
    public async Task RemappingDuringObservationInvalidatesProofWithoutRerouting()
    {
        var original = Binding();
        var account = new PFlowPersonalAccount
        {
            Binding = original,
            Pages = [new() { PageId = "456", VerifiedRunId = "old-run" }],
        };
        var navigator = new SessionNavigator
        {
            Observed = () => account.Binding = original with { DeviceIndex = 900076 },
        };
        var result = await new PFlowAccountSessionService(navigator).PreparePersonalAsync(
            account,
            default,
            true
        );
        Assert.Equal(PFlowSessionOutcome.BindingMismatch, result.Outcome);
        Assert.False(result.IsReady);
        Assert.Equal(original, result.Binding);
        Assert.Empty(account.Pages[0].VerifiedRunId);
        Assert.Equal(0, navigator.Switches);
        Assert.All(navigator.Indices, i => Assert.Equal(900075, i));
    }

    [Fact]
    public async Task StructuredCancellationDoesNotNavigateOrMarkReady()
    {
        var navigator = new SessionNavigator();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new PFlowAccountSessionService(navigator).PreparePersonalAsync(
            new() { Binding = Binding() },
            cancellation.Token,
            true
        );
        Assert.Equal(PFlowSessionOutcome.Cancelled, result.Outcome);
        Assert.False(result.IsReady);
        Assert.Empty(navigator.Indices);
    }

    private sealed class SessionNavigator : IPFlowAccountSessionNavigator
    {
        public Action? Observed;
        public int Switches;
        public List<int> Indices = [];

        public Task<PFlowWorkerIdentity> ReadWorkerAsync(
            PFlowAccountBinding binding,
            CancellationToken ct
        )
        {
            Indices.Add(binding.DeviceIndex);
            return Task.FromResult(
                new PFlowWorkerIdentity(
                    binding.DeviceIndex,
                    binding.DeviceName,
                    binding.InstanceIdentity,
                    binding.AppPackage
                )
            );
        }

        public Task<PFlowAccountObservation> ObserveAsync(
            PFlowAccountBinding binding,
            CancellationToken ct
        )
        {
            Indices.Add(binding.DeviceIndex);
            Observed?.Invoke();
            return Task.FromResult(
                new PFlowAccountObservation(
                    binding.OwnerUid,
                    binding.AppPackage,
                    true,
                    true,
                    true,
                    DateTimeOffset.UtcNow,
                    PFlowAuthenticationState.Authenticated
                )
            );
        }

        public Task<bool> SwitchSavedPersonalAccountAsync(
            PFlowAccountBinding binding,
            CancellationToken ct
        )
        {
            Switches++;
            Indices.Add(binding.DeviceIndex);
            return Task.FromResult(false);
        }
    }

    public class Device : DispatchProxy
    {
        public string FocusedActivity =
            "mCurrentFocus=Window{fixture com.facebook.katana/ProfileActivity}";
        public bool Running;
        public int Network = 100;
        public CancellationTokenSource? CancelOnInternet;
        public List<string> Calls = [];
        public List<int> Indices = [];
        public string Ui = "",
            AfterSubmit = "";
        public int Submissions;

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
                            Index = 900075,
                            Name = "fixture-LD",
                            IsAndroidStarted = Running,
                        },
                    ]);
                case "ExecuteAdbAsync":
                    CancelOnInternet?.Cancel();
                    return Task.FromResult(
                        CancelOnInternet == null
                            ? "Active default network: "
                                + Network
                                + "\nCurrent Networks:\n NetworkAgentInfo{network{"
                                + Network
                                + "} ni{[state: CONNECTED/CONNECTED]} nc{[Capabilities: INTERNET&VALIDATED]}}\n"
                            : "offline"
                    );
                case "LaunchInstanceAsync":
                    Running = true;
                    return Task.FromResult(true);
                case "WaitForBootCompletedAsync":
                case "EnsureAtxAgentRunningAsync":
                    return Task.FromResult(true);
                case "LaunchAppAsync":
                    FocusedActivity =
                        "mCurrentFocus=Window{fixture com.facebook.katana/ProfileActivity}";
                    return Task.FromResult(true);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(FocusedActivity);
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Ui);
                case "IsAdbKeyboardActiveAsync":
                    return Task.FromResult(true);
                case "InputTextAdbKeyboardAsync":
                    var doc = XDocument.Parse(Ui);
                    doc.Descendants("node")
                        .Single(n => (string?)n.Attribute("class") == "android.widget.EditText")
                        .SetAttributeValue("text", (string)args![1]!);
                    foreach (
                        var node in doc.Descendants("node")
                            .Where(n => (string?)n.Attribute("content-desc") == "Continue")
                    )
                        node.SetAttributeValue("enabled", "true");
                    Ui = doc.ToString();
                    return Task.FromResult(true);
                case "TapAsync":
                    var focusDoc = XDocument.Parse(Ui);
                    foreach (
                        var input in focusDoc
                            .Descendants("node")
                            .Where(n => (string?)n.Attribute("class") == "android.widget.EditText")
                    )
                        input.SetAttributeValue("focused", "true");
                    Ui = focusDoc.ToString();
                    var submit = PFlowNativeLoginNavigator.Control(Ui, "Continue");
                    if (
                        submit is { } point
                        && (int)args![1]! == point.X
                        && (int)args[2]! == point.Y
                    )
                    {
                        Submissions++;
                        Ui = AfterSubmit;
                    }
                    return Task.FromResult(true);
                default:
                    throw new InvalidOperationException(
                        "Unexpected mocked device operation: " + method.Name
                    );
            }
        }
    }
}
