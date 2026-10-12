using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowAutomaticSessionTests
{
    private static PFlowAccountBinding Binding(string uid = "123", int index = 900181) =>
        new("fixture-identity", index, "fixture-LD", "com.facebook.katana", uid);

    private static PFlowPersonalAccount Account(string uid = "123") =>
        new()
        {
            Binding = Binding(uid),
            Name = "Fixture " + uid,
            CredentialReference = "fixture-reference",
            AllowAutomaticLogin = true,
            AllowAccountRotation = true,
            Pages = [new() { PageId = "77", VerifiedRunId = "stale-run" }],
        };

    private static PFlowAccountObservation Observation(
        string uid,
        PFlowAuthenticationState state = PFlowAuthenticationState.Authenticated
    ) =>
        new(
            uid,
            "com.facebook.katana",
            true,
            true,
            state == PFlowAuthenticationState.Authenticated,
            DateTimeOffset.UtcNow,
            state
        );

    private static Task Progress(string _) => Task.CompletedTask;

    private sealed class Navigator : IPFlowAutomaticAccountNavigator
    {
        public string ActiveUid = "123";
        public PFlowAuthenticationState State = PFlowAuthenticationState.Authenticated;
        public bool WrongAfterLogin,
            Stale,
            WrongWorker,
            ThrowOnLogin;
        public int Observations,
            Logins,
            Opens;
        public bool? LogoutAllowed;
        public Action? Observed;
        public List<PFlowAccountBinding> Bindings = [];

        public Task<PFlowWorkerIdentity> ReadWorkerAsync(
            PFlowAccountBinding binding,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Bindings.Add(binding);
            return Task.FromResult(
                new PFlowWorkerIdentity(
                    WrongWorker ? binding.DeviceIndex + 1 : binding.DeviceIndex,
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
            ct.ThrowIfCancellationRequested();
            Observations++;
            Observed?.Invoke();
            var observed = Observation(ActiveUid, State);
            return Task.FromResult(
                Stale
                    ? observed with
                    {
                        ObservedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    }
                    : observed
            );
        }

        public Task OpenLoginAsync(
            PFlowAccountBinding binding,
            bool allowLogout,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Opens++;
            LogoutAllowed = allowLogout;
            return Task.CompletedTask;
        }

        public Task<PFlowAccountObservation> LoginAsync(
            PFlowAccountBinding binding,
            PFlowCredential credential,
            Func<string, Task> progress,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Logins++;
            if (ThrowOnLogin)
                throw new InvalidOperationException("Fixture unknown login result");
            ActiveUid = WrongAfterLogin ? "999" : binding.OwnerUid;
            State = PFlowAuthenticationState.Authenticated;
            return Task.FromResult(Observation(ActiveUid));
        }
    }

    private sealed class Credentials : IPFlowCredentialStore
    {
        public int Loads;
        public bool Missing;
        public PFlowCredential? Last;

        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding binding,
            string reference,
            CancellationToken ct = default
        )
        {
            Loads++;
            if (Missing)
                throw new IOException("Fixture missing credentials");
            Last = PFlowCredentialImport.Parse(
                binding.OwnerUid + "|fixture-password||",
                binding,
                "fixture@example.invalid",
                PFlowTwoFactorKind.None
            );
            return Task.FromResult(Last);
        }

        public Task<string> SaveAsync(
            PFlowAccountBinding binding,
            PFlowCredential credential,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public void Delete(string reference) => throw new NotSupportedException();
    }

    private static PFlowAutomaticSessionService Service(
        Navigator navigator,
        Credentials store,
        Func<CancellationToken, Task>? idle = null
    ) => new(navigator, store, idle ?? (_ => Task.CompletedTask));

    [Fact]
    public async Task ExistingSessionNeedsNeitherPasswordNorRotationPermission()
    {
        var account = Account();
        account.AllowAutomaticLogin = account.AllowAccountRotation = false;
        var nav = new Navigator();
        var store = new Credentials();
        await Service(nav, store).EnsureAsync(account, Progress, default, true);
        Assert.Equal(PFlowAuthenticationState.Authenticated, account.Authentication);
        Assert.Empty(account.Pages[0].VerifiedRunId);
        Assert.Equal(0, store.Loads);
        Assert.Equal(0, nav.Logins);
        Assert.Equal(0, nav.Opens);
    }

    [Fact]
    public async Task ExpiredSessionRecoversWithoutLogout()
    {
        var account = Account();
        account.AllowAccountRotation = false;
        var nav = new Navigator { State = PFlowAuthenticationState.LoginRequired };
        var store = new Credentials();
        await Service(nav, store).EnsureAsync(account, Progress, default, true);
        Assert.False(nav.LogoutAllowed);
        Assert.Equal(1, nav.Logins);
        Assert.True(store.Last!.IsDisposed);
        Assert.Equal(PFlowAuthenticationState.Authenticated, account.Authentication);
    }

    [Fact]
    public async Task DifferentAccountLoadsBoundCredentialsThenRotates()
    {
        var account = Account("456");
        var nav = new Navigator();
        var store = new Credentials();
        await Service(nav, store).EnsureAsync(account, Progress, default, true);
        Assert.True(nav.LogoutAllowed);
        Assert.Equal("456", nav.ActiveUid);
        Assert.All(nav.Bindings, b => Assert.Equal(account.Binding, b));
        Assert.True(store.Last!.IsDisposed);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AccountPoliciesBlockBeforeLogout(bool login, bool rotation)
    {
        var account = Account("456");
        account.AllowAutomaticLogin = login;
        account.AllowAccountRotation = rotation;
        var nav = new Navigator();
        var store = new Credentials();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(nav, store).EnsureAsync(account, Progress, default, true)
        );
        Assert.Equal(0, store.Loads);
        Assert.Equal(0, nav.Opens);
        Assert.Equal(0, nav.Logins);
    }

    [Fact]
    public async Task MissingCredentialsPreserveCurrentAccount()
    {
        var nav = new Navigator();
        var store = new Credentials { Missing = true };
        await Assert.ThrowsAsync<IOException>(() =>
            Service(nav, store).EnsureAsync(Account("456"), Progress, default, true)
        );
        Assert.Equal("123", nav.ActiveUid);
        Assert.Equal(0, nav.Opens);
    }

    [Theory]
    [InlineData(PFlowAuthenticationState.Checkpoint)]
    [InlineData(PFlowAuthenticationState.Unavailable)]
    [InlineData(PFlowAuthenticationState.VerificationFailed)]
    public async Task UnsupportedOrRejectedAuthenticationNeverAttemptsPassword(
        PFlowAuthenticationState state
    )
    {
        var nav = new Navigator { State = state };
        var store = new Credentials();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(nav, store).EnsureAsync(Account(), Progress, default, true)
        );
        Assert.Equal(0, nav.Logins);
        Assert.Equal(0, nav.Opens);
        Assert.Equal(0, store.Loads);
    }

    [Fact]
    public async Task WrongAccountAfterAcceptedLoginCannotStartTasks()
    {
        var account = Account("456");
        var nav = new Navigator { WrongAfterLogin = true };
        var store = new Credentials();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(nav, store).EnsureAsync(account, Progress, default, true)
        );
        Assert.Equal(PFlowAuthenticationState.VerificationFailed, account.Authentication);
        Assert.Null(account.LastVerifiedUtc);
        Assert.True(store.Last!.IsDisposed);
    }

    [Fact]
    public async Task UncertainLoginIsNeverRetriedInSameBatch()
    {
        var nav = new Navigator { ThrowOnLogin = true };
        var store = new Credentials();
        var service = Service(nav, store);
        var account = Account("456");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureAsync(account, Progress, default, true)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureAsync(account, Progress, default, true)
        );
        Assert.Equal(1, nav.Logins);
        Assert.Equal(1, nav.Opens);
        Assert.Equal(1, store.Loads);
        Assert.True(store.Last!.IsDisposed);
    }

    [Fact]
    public async Task UnknownUploadBlocksEvenObservation()
    {
        var nav = new Navigator();
        var store = new Credentials();
        var service = Service(
            nav,
            store,
            _ => throw new InvalidOperationException("Fixture uncertain upload")
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureAsync(Account("456"), Progress, default, true)
        );
        Assert.Equal(0, nav.Observations);
        Assert.Equal(0, store.Loads);
        Assert.Equal(0, nav.Opens);
    }

    [Fact]
    public async Task UploadStartingDuringObservationBlocksLogout()
    {
        var nav = new Navigator();
        var store = new Credentials();
        var idleChecks = 0;
        var service = Service(
            nav,
            store,
            _ =>
                ++idleChecks == 2
                    ? throw new InvalidOperationException("Fixture upload started")
                    : Task.CompletedTask
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureAsync(Account("456"), Progress, default, true)
        );
        Assert.Equal(0, nav.Opens);
        Assert.Equal(0, nav.Logins);
        Assert.True(store.Last!.IsDisposed);
    }

    [Fact]
    public async Task WorkerMismatchDoesNotObserveOrEnterCredentials()
    {
        var nav = new Navigator { WrongWorker = true };
        var store = new Credentials();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(nav, store).EnsureAsync(Account(), Progress, default, true)
        );
        Assert.Equal(0, nav.Observations);
        Assert.Equal(0, store.Loads);
    }

    [Fact]
    public async Task RemapDuringObservationInvalidatesSession()
    {
        var account = Account();
        var nav = new Navigator();
        var store = new Credentials();
        nav.Observed = () => account.Binding = Binding("456", 900182);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(nav, store).EnsureAsync(account, Progress, default, true)
        );
        Assert.Equal(0, store.Loads);
        Assert.Equal(0, nav.Opens);
    }

    [Fact]
    public async Task StaleUidProofDoesNotAuthorizeReuseOrLogout()
    {
        var nav = new Navigator { Stale = true };
        var store = new Credentials();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(nav, store).EnsureAsync(Account(), Progress, default, true)
        );
        Assert.Equal(0, nav.Opens);
        Assert.Equal(0, store.Loads);
    }

    [Fact]
    public async Task CancellationDoesNotAuthenticate()
    {
        var account = Account();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(new(), new()).EnsureAsync(account, Progress, cancellation.Token, true)
        );
        Assert.NotEqual(PFlowAuthenticationState.Authenticated, account.Authentication);
    }

    [Fact]
    public void PlanScopesSelectionsAndRespectsOrder()
    {
        var a = Account();
        a.SelectedForPosting = true;
        a.RotationOrder = 2;
        var b = Account("456");
        b.SelectedForScreenTime = true;
        b.RotationOrder = 1;
        var unselected = Account("777");
        var otherLd = Account("888");
        otherLd.Binding = Binding("888", 900182);
        otherLd.SelectedForPosting = true;
        Assert.Equal(
            new[] { "456", "123" },
            PFlowAutomaticSessionService
                .Plan([a, b, unselected, otherLd], 900181, "com.facebook.katana", true, true)
                .Select(x => x.Binding.OwnerUid)
        );
    }

    [Fact]
    public async Task BatchFinishesAccountBeforeRotatingToNext()
    {
        var a = Account();
        var b = Account("456");
        a.SelectedForScreenTime = a.SelectedForPosting = true;
        b.SelectedForScreenTime = b.SelectedForPosting = true;
        var calls = new List<string>();
        Task Record(string step, PFlowPersonalAccount account)
        {
            calls.Add(step + ":" + account.Binding.OwnerUid);
            return Task.CompletedTask;
        }
        await PFlowAccountBatchRunner.RunAsync(
            [a, b],
            (x, _) => Record("ensure", x),
            (x, _) => Record("screen", x),
            (x, _) => Record("post", x),
            (x, _) => Record("return", x),
            _ => Task.CompletedTask,
            default
        );
        Assert.Equal(
            new[]
            {
                "ensure:123",
                "screen:123",
                "post:123",
                "return:123",
                "ensure:456",
                "screen:456",
                "post:456",
                "return:456",
            },
            calls
        );
    }

    [Fact]
    public async Task FinalProfilePreferenceStillRestoresIntermediateAccount()
    {
        var returned = new List<string>();
        await PFlowAccountBatchRunner.RunAsync(
            [Account(), Account("456")],
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (a, _) =>
            {
                returned.Add(a.Binding.OwnerUid);
                return Task.CompletedTask;
            },
            _ => Task.CompletedTask,
            default,
            returnLastPersonal: false
        );
        Assert.Equal(new[] { "123" }, returned);
    }

    [Fact]
    public async Task WrongLoginInsideBatchPreventsAllAccountActions()
    {
        var account = Account("456");
        account.SelectedForPosting = account.SelectedForScreenTime = true;
        var service = Service(new Navigator { WrongAfterLogin = true }, new Credentials());
        var actions = 0;
        Task Action(PFlowPersonalAccount _, CancellationToken ct)
        {
            actions++;
            return Task.CompletedTask;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowAccountBatchRunner.RunAsync(
                [account],
                (a, c) => service.EnsureAsync(a, Progress, c, true),
                Action,
                Action,
                Action,
                _ => Task.CompletedTask,
                default
            )
        );
        Assert.Equal(0, actions);
    }

    [Theory]
    [InlineData("123", false)]
    [InlineData("789", true)]
    public async Task AddAccountReusesSessionOrChangesOnlyItsAssignedLogin(
        string requested,
        bool logout
    )
    {
        var nav = new Navigator();
        var adapter = new PFlowAddAccountLoginNavigator(
            nav,
            (_, _, _) => Task.CompletedTask,
            _ => Task.CompletedTask
        );
        using var credential = PFlowCredentialImport.Parse(
            requested + "|fixture-password||",
            Binding(requested),
            "fixture@example.invalid",
            PFlowTwoFactorKind.None
        );
        var observed = await adapter.LoginAsync(Binding(requested), credential, Progress, default);
        Assert.True(PFlowAutomaticSessionService.Verified(observed, Binding(requested)));
        Assert.Equal(logout ? 1 : 0, nav.Logins);
        Assert.Equal(logout ? 1 : 0, nav.Opens);
    }

    [Fact]
    public async Task AddAccountCannotLogOutWhilePublicationIsUncertain()
    {
        var nav = new Navigator();
        var adapter = new PFlowAddAccountLoginNavigator(
            nav,
            (_, _, _) => Task.CompletedTask,
            _ => throw new InvalidOperationException("Fixture unresolved publication")
        );
        using var credential = PFlowCredentialImport.Parse(
            "456|fixture-password||",
            Binding("456"),
            "fixture@example.invalid",
            PFlowTwoFactorKind.None
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.LoginAsync(Binding("456"), credential, Progress, default)
        );
        Assert.Equal(0, nav.Observations);
        Assert.Equal(0, nav.Opens);
        Assert.Equal(0, nav.Logins);
    }

    [Fact]
    public async Task AccountPoliciesSurviveReloadWithoutReusingAuthentication()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "pflow-rotation-options-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            var store = new PFlowAccountStore(Path.Combine(directory, "accounts.json"));
            var account = Account();
            account.RotationOrder = 7;
            account.Authentication = PFlowAuthenticationState.Authenticated;
            await store.UpdateAsync(accounts => accounts.Add(account));
            var loaded = Assert.Single(await store.LoadAsync());
            Assert.True(loaded.AllowAutomaticLogin);
            Assert.True(loaded.AllowAccountRotation);
            Assert.Equal(7, loaded.RotationOrder);
            Assert.Equal(account.CredentialReference, loaded.CredentialReference);
            Assert.Equal(PFlowAuthenticationState.Cached, loaded.Authentication);
            Assert.Empty(loaded.Pages[0].VerifiedRunId);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void NegativeOrderAndDuplicateMappingsCannotBecomeAPlan()
    {
        var account = Account();
        account.SelectedForPosting = true;
        account.RotationOrder = -1;
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAutomaticSessionService.Plan([account], 900181, "com.facebook.katana", false, true)
        );
        account.RotationOrder = 0;
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAutomaticSessionService.Plan(
                [account, account],
                900181,
                "com.facebook.katana",
                false,
                true
            )
        );
    }

    [Fact]
    public async Task UnknownAfterPostingPreventsReturnAndNextAccount()
    {
        var a = Account();
        a.SelectedForPosting = true;
        var b = Account("456");
        var uncertain = false;
        var ensured = new List<string>();
        var returned = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowAccountBatchRunner.RunAsync(
                [a, b],
                (x, _) =>
                {
                    ensured.Add(x.Binding.OwnerUid);
                    return Task.CompletedTask;
                },
                (_, _) => Task.CompletedTask,
                (_, _) =>
                {
                    uncertain = true;
                    return Task.CompletedTask;
                },
                (_, _) =>
                {
                    returned = true;
                    return Task.CompletedTask;
                },
                _ =>
                    uncertain
                        ? throw new InvalidOperationException("Fixture unknown publication")
                        : Task.CompletedTask,
                default
            )
        );
        Assert.Equal(new[] { "123" }, ensured);
        Assert.False(returned);
    }
}
