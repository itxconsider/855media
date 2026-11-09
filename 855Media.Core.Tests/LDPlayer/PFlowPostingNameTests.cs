using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPostingNameTests
{
    private static PFlowAccountBinding Binding =>
        new("fixture", 900755, "fixture-LD", "com.facebook.katana", "100000000000001");

    private static FacebookPageTarget Target(
        string name = "Z Outdoors+",
        string id = "100000000000002"
    ) =>
        new()
        {
            PageId = id,
            UserUid = Binding.OwnerUid,
            PageName = name,
            Type = "Page",
            AccountBinding = Binding,
        };

    private static string Profile(string name = "Z Outdoors+") =>
        File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowLayouts",
                    "ld56-page-business-dashboard.xml"
                )
            )
            .Replace("Z Outdoors+", name)
            .Replace(
                "</hierarchy>",
                $"<node package='com.facebook.katana' content-desc='{name}, see your profile' bounds='[48,108][405,188]'/></hierarchy>"
            );

    private static (ILDPlayerService Ld, Device Fake) Fake()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    private static PFlowJob Job(
        FacebookPageTarget target,
        PFlowNativeIdentityScope? scope = null
    ) =>
        new()
        {
            BatchId = "name-batch",
            AccountBinding = Binding,
            DeviceIndex = Binding.DeviceIndex,
            AppPackage = Binding.AppPackage,
            TargetId = target.PageId,
            TargetName = target.PageName,
            OwnerUid = target.UserUid,
            PersonaUid = target.PersonaUid,
            IsProfile = target.IsPrimaryProfile,
            PostType = "Reel",
            NativeIdentityScope = scope,
        };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedProfileDestinationUsesTitleWithoutEditOrDashboardRights(bool removeRights)
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld20-postswitch-profile-captured.xml"
            )
        );
        var doc = XDocument.Parse(xml);
        if (removeRights)
            foreach (
                var a in doc.Descendants("node")
                    .Attributes()
                    .Where(a =>
                        a.Name.LocalName is "text" or "content-desc"
                        && (a.Value.Contains("Edit profile") || a.Value.Contains("Dashboard"))
                    )
                    .ToArray()
            )
                a.Value = "";
        Assert.Equal("Fixture Page", PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyMediaPickerCannotBeUsedAsAConfirmedPostingContinuation(bool descriptionOnly)
    {
        var key = descriptionOnly ? "content-desc" : "text";
        var xml =
            $"<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node {key}='New post'/><node {key}='Gallery'/><node {key}='Select multiple'/></node></hierarchy>";
        Assert.False(PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana"));
    }

    [Fact]
    public async Task FreshActivationRebindsOnlyAfterDestinationAndStableProcess()
    {
        var (ld, fake) = Fake();
        fake.ProcessReads = new Queue<string>(["123", "456", "456", "456"]);
        var target = Target();
        var scope = await PFlowPostingNameVerification.ActivateAsync(
            ld,
            Binding,
            target,
            [target],
            null,
            default
        );
        Assert.True(scope.Matches(Binding, "456", target));
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, fake.UnexpectedReads);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(17)]
    [InlineData(23)]
    public async Task CapturedActivePageUsesHeaderWithoutReopeningSwitcher(int ldIndex)
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                $"ld{ldIndex}-posting-menu-captured.xml"
            )
        );
        var (ld, fake) = Fake();
        fake.Xml = xml;
        Assert.True(PFlowSessionProfileUi.Safe(xml), "profile safe");
        Assert.True(
            PFlowPostTargetSession.CanContinueFrom(xml, "com.facebook.katana"),
            "continuation safe"
        );
        var size = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        Assert.True(size.Height > 500, $"height {size.Height}");
        Assert.Equal("Fixture Page", PFlowPostingNameVerification.ActiveHeaderName(xml));
        var actor = await PFlowPostingNameVerification.ObserveAccountAsync(
            ld,
            Binding,
            "Fixture Personal",
            null,
            default
        );
        Assert.Equal(new PFlowPostingNameVerification.Actor("Fixture Page", "Page"), actor);
        var target = Target("Fixture Page");
        var scope = await PFlowPostingNameVerification.ActivateAsync(
            ld,
            Binding,
            target,
            [target],
            null,
            default
        );
        for (var repeat = 0; repeat < 3; repeat++)
            Assert.True(await scope.VerifyAsync(ld, null, default));
        Assert.Equal(0, fake.Taps);
        Assert.Equal(0, fake.Switches);
        Assert.Equal(0, fake.UnexpectedReads);
    }

    [Theory]
    [InlineData("wrong-name")]
    [InlineData("hidden")]
    [InlineData("truncated")]
    [InlineData("duplicate")]
    [InlineData("disabled")]
    [InlineData("clipped")]
    [InlineData("input")]
    [InlineData("foreign")]
    public void ActiveHeaderRequiresOneCompleteVisibleName(string mutation)
    {
        var xml = Profile();
        var doc = XDocument.Parse(xml);
        var header = doc.Descendants("node")
            .Single(n =>
                ((string?)n.Attribute("content-desc"))?.EndsWith(", see your profile") == true
            );
        if (mutation == "wrong-name")
            header.SetAttributeValue("content-desc", "Other Page, see your profile");
        if (mutation == "hidden")
            header.SetAttributeValue("visible-to-user", "false");
        if (mutation == "truncated")
            header.SetAttributeValue("content-desc", "Z Out\u2026, see your profile");
        if (mutation == "duplicate")
        {
            var other = new XElement(header);
            other.SetAttributeValue("content-desc", "Other Page, see your profile");
            header.Parent!.Add(other);
        }
        if (mutation == "disabled")
            header.SetAttributeValue("enabled", "false");
        if (mutation == "clipped")
            header.SetAttributeValue("bounds", "[0,108][0,188]");
        if (mutation == "input")
            header.Parent!.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.EditText")
                )
            );
        if (mutation == "foreign")
            header.Parent!.Add(new XElement("node", new XAttribute("package", "foreign.app")));
        var name = PFlowPostingNameVerification.ActiveHeaderName(doc.ToString());
        if (mutation == "wrong-name")
            Assert.Equal("Other Page", name);
        else
            Assert.Null(name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedSelectedRowConfirmsPageWithoutProfileButtons(bool wrongName)
    {
        var target = Target("Bushcraft improvisation");
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld55-switcher-selected-page-captured.xml"
            )
        );
        Assert.DoesNotContain("Edit profile", xml);
        if (wrongName)
            target.PageName = "Unrelated Name";
        var scope = PFlowNativeIdentityScope.FromVerifiedName(
            Binding,
            "123",
            target,
            xml,
            [target]
        );
        Assert.Equal(!wrongName, scope != null);
    }

    [Fact]
    public async Task SelectedNameProofStartsPostingWithoutAnyOwnProfileButton()
    {
        var target = Target();
        var scope = PFlowNativeIdentityScope.FromSelectedName(
            Binding,
            "123",
            target,
            new(target.PageName, "Page"),
            [target]
        );
        Assert.NotNull(scope);
        var xml =
            "<hierarchy><node package='com.facebook.katana' text='Home' bounds='[0,0][720,1280]'/></hierarchy>";
        Assert.True(
            await new PFlowPostTargetSession().VerifyAsync(
                Job(target, scope),
                _ => Task.FromResult("Database UI name"),
                _ => throw new Exception("Posting identity must not require profile buttons"),
                _ => Task.FromResult(xml),
                default
            )
        );
    }

    [Theory]
    [InlineData("Other Page", "Page")]
    [InlineData("Z Outdoors+", "Profile")]
    [InlineData("Z Outdoors+ extra", "Page")]
    public void SelectedProofRejectsWrongCompleteNameOrType(string name, string type)
    {
        var target = Target();
        Assert.Null(
            PFlowNativeIdentityScope.FromSelectedName(
                Binding,
                "123",
                target,
                new(name, type),
                [target]
            )
        );
    }

    [Fact]
    public async Task TenConsecutiveReelsUseNameProofWithNoUidOrGraphReads()
    {
        var (ld, fake) = Fake();
        var target = Target();
        var scope = await PFlowPostingNameVerification.ActivateAsync(
            ld,
            Binding,
            target,
            [target],
            null,
            default
        );
        Assert.True(scope.UsesDatabaseName);
        var session = new PFlowPostTargetSession();
        var activations = 0;
        for (var i = 0; i < 10; i++)
        {
            var job = Job(target, scope);
            Assert.True(
                await session.VerifyAsync(
                    job,
                    _ =>
                    {
                        activations++;
                        return Task.FromResult("Database UI name");
                    },
                    _ => Task.FromResult(true),
                    _ => Task.FromResult(fake.Xml),
                    default,
                    fake.Xml,
                    verifyContinuationSession: async ct =>
                        Assert.True(
                            await scope.VerifyContinuationAsync(job, fake.Xml, ld, null, ct)
                        )
                )
            );
            // The real repeat/publication identity route also accepts this explicit name scope.
            Assert.True(await scope.VerifyAsync(ld, null, default));
            job.State = PFlowState.Confirmed;
            job.ConfirmedUtc = DateTime.UtcNow;
            job.Evidence = "fixture publication proof";
        }
        Assert.Equal(1, activations);
        Assert.Equal(0, fake.Switches);
        Assert.Equal(0, fake.UnexpectedReads);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task DifferentPagesActivateBySavedNameWithoutOwnerUidReads()
    {
        var (ld, fake) = Fake();
        var pages = Enumerable
            .Range(0, 3)
            .Select(i => Target("Page " + i, "10000000000000" + (i + 2)))
            .ToArray();
        foreach (var target in pages)
        {
            fake.Xml = Profile(target.PageName);
            var scope = await PFlowPostingNameVerification.ActivateAsync(
                ld,
                Binding,
                target,
                pages,
                null,
                default
            );
            Assert.True(scope.Matches(Job(target)));
        }
        Assert.Equal(0, fake.Switches);
        Assert.Equal(0, fake.UnexpectedReads);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("not-saved")]
    [InlineData("wrong-binding")]
    [InlineData("wrong-name")]
    [InlineData("wrong-type")]
    [InlineData("input")]
    public void NameFactoryRejectsAmbiguousMissingOrUnsafeEvidence(string change)
    {
        var target = Target();
        FacebookPageTarget[] saved = [target];
        var xml = Profile();
        if (change == "duplicate")
            saved = [target, Target("ZOutdoors+", "100000000000099")];
        if (change == "not-saved")
            saved = [];
        if (change == "wrong-binding")
            target.AccountBinding = Binding with { DeviceIndex = 56 };
        if (change == "wrong-name")
            xml = Profile("Z Outdoors+ Extra");
        if (change == "wrong-type")
            target.Type = "Profile";
        if (change == "input")
            xml = xml.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
            );
        Assert.Null(PFlowNativeIdentityScope.FromVerifiedName(Binding, "123", target, xml, saved));
    }

    [Fact]
    public async Task DuplicateDatabaseNameIsRejectedBeforeSwitching()
    {
        var (ld, fake) = Fake();
        var target = Target();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowPostingNameVerification.ActivateAsync(
                ld,
                Binding,
                target,
                [target, Target("ZOutdoors+", "100000000000099")],
                null,
                default
            )
        );
        Assert.Equal(0, fake.Switches);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("foreground")]
    [InlineData("name")]
    public async Task ChangedPostingProofIsInvalidatedWithoutUidFallback(string change)
    {
        var (ld, fake) = Fake();
        var target = Target();
        var scope = await PFlowPostingNameVerification.ActivateAsync(
            ld,
            Binding,
            target,
            [target],
            null,
            default
        );
        if (change == "process")
            fake.Pid = "999";
        if (change == "foreground")
            fake.Activity = "other.app/Main";
        if (change == "name")
            fake.Xml = Profile("Other Page");
        Assert.False(await scope.VerifyAsync(ld, null, default));
        fake.Pid = "123";
        fake.Activity = Binding.AppPackage + "/Main";
        fake.Xml = Profile();
        Assert.False(await scope.VerifyAsync(ld, null, default));
        Assert.Equal(0, fake.UnexpectedReads);
    }

    [Fact]
    public async Task NameLabelAloneCannotAuthorizeWithoutMatchingRuntimeScope()
    {
        var session = new PFlowPostTargetSession();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.VerifyAsync(
                Job(Target()),
                _ => Task.FromResult("Database UI name"),
                _ => Task.FromResult(true),
                _ => Task.FromResult(Profile()),
                default
            )
        );
    }

    [Fact]
    public async Task NameScopeCannotMasqueradeAsGraphBoundSessionEvidence()
    {
        var (ld, _) = Fake();
        var target = Target();
        var scope = await PFlowPostingNameVerification.ActivateAsync(
            ld,
            Binding,
            target,
            [target],
            null,
            default
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowPostTargetSession().VerifyAsync(
                Job(target, scope),
                _ => Task.FromResult("Session UI"),
                _ => Task.FromResult(true),
                _ => Task.FromResult(Profile()),
                default
            )
        );
    }

    [Fact]
    public void ActiveTargetPreferenceUsesCompleteSavedNameAndType()
    {
        var a = Target();
        var b = Target("Other Page", "100000000000003");
        Assert.Same(
            b,
            PFlowPostingNameVerification.PreferActive(
                [a, b],
                t => t,
                Binding,
                new("OtherPage", "Page")
            )[0]
        );
        Assert.Same(
            a,
            PFlowPostingNameVerification.PreferActive(
                [a, b],
                t => t,
                Binding,
                new("Other", "Page")
            )[0]
        );
        Assert.Same(
            a,
            PFlowPostingNameVerification.PreferActive(
                [a, b],
                t => t,
                Binding,
                new("Other Page", "Profile")
            )[0]
        );
    }

    [Fact]
    public void NameAccountObservationDoesNotPretendToContainALiveUid()
    {
        var observed = NameObservation("Personal Owner");
        Assert.True(PFlowPostingNameVerification.Verified(observed, "PersonalOwner"));
        Assert.False(PFlowAutomaticSessionService.Verified(observed, Binding));
        Assert.False(PFlowPostingNameVerification.Verified(observed, "Another Owner"));
        Assert.False(
            PFlowPostingNameVerification.Verified(
                observed with
                {
                    ObservedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                },
                "Personal Owner"
            )
        );
    }

    private static PFlowAccountObservation NameObservation(string name) =>
        new(
            "",
            Binding.AppPackage,
            true,
            true,
            false,
            DateTimeOffset.UtcNow,
            PFlowAuthenticationState.Authenticated
        )
        {
            VerifiedName = name,
        };

    [Fact]
    public async Task AutomaticPostingSessionAcceptsNameEvidenceWhileDefaultUidPolicyRejectsIt()
    {
        var navigator = new NameNavigator();
        var account = new PFlowPersonalAccount { Binding = Binding, Name = "Personal Owner" };
        var service = new PFlowAutomaticSessionService(
            navigator,
            new NoCredentials(),
            _ => Task.CompletedTask,
            postingNameOnly: true
        );
        await service.EnsureAsync(
            account,
            _ => Task.CompletedTask,
            default,
            deviceLeaseAlreadyHeld: true
        );
        Assert.Equal(PFlowAuthenticationState.Authenticated, account.Authentication);
        Assert.Equal(0, navigator.Logins);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowAutomaticSessionService(
                navigator,
                new NoCredentials(),
                _ => Task.CompletedTask
            ).EnsureAsync(account, _ => Task.CompletedTask, default, deviceLeaseAlreadyHeld: true)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NameOnlyAccountRotationKeepsPermissionAndCredentialGuards(bool allowRotation)
    {
        var navigator = new NameNavigator { CurrentName = "Another Owner" };
        var account = new PFlowPersonalAccount
        {
            Binding = Binding,
            Name = "Personal Owner",
            AllowAutomaticLogin = true,
            AllowAccountRotation = allowRotation,
            CredentialReference = "fixture",
        };
        var credentials = new RotationCredentials();
        var service = new PFlowAutomaticSessionService(
            navigator,
            credentials,
            _ => Task.CompletedTask,
            postingNameOnly: true
        );
        Task Run() =>
            service.EnsureAsync(
                account,
                _ => Task.CompletedTask,
                default,
                deviceLeaseAlreadyHeld: true
            );
        if (allowRotation)
        {
            await Run();
            Assert.Equal(1, navigator.Logins);
            Assert.True(navigator.LogoutAllowed);
            Assert.Equal(1, credentials.Loads);
            Assert.Equal(PFlowAuthenticationState.Authenticated, account.Authentication);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(Run);
            Assert.Equal(0, navigator.Logins);
            Assert.Equal(0, credentials.Loads);
        }
    }

    public class Device : DispatchProxy
    {
        public string Xml = Profile(),
            Pid = "123",
            Activity = Binding.AppPackage + "/Main";
        public int Switches,
            UnexpectedReads,
            Taps;

        public Queue<string> ProcessReads = new();
        private string? _underSheet;

        private string Sheet()
        {
            var doc = XDocument.Load(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowScreenTime",
                    "ld55-switcher-selected-page-captured.xml"
                )
            );
            var name =
                ScreenTimeHierarchy
                    .Nodes(Xml)
                    .Select(n => ((string?)n.Attribute("content-desc"))?.Trim())
                    .FirstOrDefault(n =>
                        n == "Other Page" || n == "Z Outdoors+" || n?.StartsWith("Page ") == true
                    )
                ?? "Z Outdoors+";
            return doc.ToString().Replace("Bushcraft improvisation", name);
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (args?.LastOrDefault() is CancellationToken ct)
                ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "OpenDeepLinkAsync":
                    _underSheet = Xml;
                    Xml = Sheet();
                    return Task.FromResult(true);
                case "PressBackAsync":
                    Xml = _underSheet ?? Profile();
                    return Task.FromResult(true);
                case "SwitchToPersonaAsync":
                    Switches++;
                    Xml = Profile(((FacebookPageTarget)args![1]!).PageName);
                    return Task.FromResult(true);
                case "ExecuteAdbAsync":
                    if ((string)args![1]! == "shell pidof com.facebook.katana")
                        return Task.FromResult(
                            ProcessReads.Count > 0 ? ProcessReads.Dequeue() : Pid
                        );
                    UnexpectedReads++;
                    throw new Exception(
                        "Posting must not read Graph credentials, owner UID or settings links"
                    );
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(Activity);
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml);
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml)
                    );
                case "TapAsync":
                    Taps++;
                    Assert.False(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(Xml));
                    _underSheet = Xml;
                    Xml = Sheet();
                    return Task.FromResult(true);
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }

    private sealed class NameNavigator : IPFlowAutomaticAccountNavigator
    {
        public int Logins;
        public string CurrentName = "Personal Owner";
        public bool LogoutAllowed;

        public Task<PFlowWorkerIdentity> ReadWorkerAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new PFlowWorkerIdentity(
                    b.DeviceIndex,
                    b.DeviceName,
                    b.InstanceIdentity,
                    b.AppPackage
                )
            );

        public Task<PFlowAccountObservation> ObserveAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        ) => Task.FromResult(NameObservation(CurrentName));

        public Task OpenLoginAsync(PFlowAccountBinding b, bool allowLogout, CancellationToken ct)
        {
            LogoutAllowed = allowLogout;
            return Task.CompletedTask;
        }

        public Task<PFlowAccountObservation> LoginAsync(
            PFlowAccountBinding b,
            PFlowCredential credential,
            Func<string, Task> progress,
            CancellationToken ct
        )
        {
            Logins++;
            CurrentName = "Personal Owner";
            return Task.FromResult(NameObservation(CurrentName));
        }
    }

    private sealed class RotationCredentials : IPFlowCredentialStore
    {
        public int Loads;

        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding b,
            string reference,
            CancellationToken ct = default
        )
        {
            Loads++;
            return Task.FromResult(
                PFlowCredentialImport.Parse(
                    b.OwnerUid + "|fixture-password||",
                    b,
                    "fixture@example.invalid",
                    PFlowTwoFactorKind.None
                )
            );
        }

        public Task<string> SaveAsync(
            PFlowAccountBinding b,
            PFlowCredential credential,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public void Delete(string reference) => throw new NotSupportedException();
    }

    private sealed class NoCredentials : IPFlowCredentialStore
    {
        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding b,
            string reference,
            CancellationToken ct = default
        ) => throw new Exception("No credentials needed");

        public Task<string> SaveAsync(
            PFlowAccountBinding b,
            PFlowCredential credential,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public void Delete(string reference) => throw new NotSupportedException();
    }
}
