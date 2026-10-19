using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowMultiAccountTests : IDisposable
{
    readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "pflow-accounts-" + Guid.NewGuid().ToString("N")
    );

    static PFlowAccountBinding Binding(int index = 1, string uid = "123") =>
        new("identity", index, "LD-" + index, "com.facebook.katana", uid);

    static PFlowPersonalAccount Account() =>
        new()
        {
            Binding = Binding(),
            Authentication = PFlowAuthenticationState.Authenticated,
            SelectedForPosting = true,
        };

    public PFlowMultiAccountTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, true);

    [Theory]
    [InlineData(2, "LD-1", "identity", "com.facebook.katana")]
    [InlineData(1, "LD-2", "identity", "com.facebook.katana")]
    [InlineData(1, "LD-1", "replacement", "com.facebook.katana")]
    [InlineData(1, "LD-1", "identity", "clone.app")]
    public void MismatchNeverRoutesElsewhere(
        int index,
        string name,
        string identity,
        string package
    ) =>
        Assert.Throws<InvalidOperationException>(() =>
            Binding().RequireWorker(index, name, identity, package)
        );

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("LD-99")]
    [InlineData("99")]
    public void UnmappedDeviceHasNoFallback(string name) =>
        Assert.Equal(
            -1,
            PFlowDeviceRouting.Resolve(
                name,
                [
                    new()
                    {
                        Index = 0,
                        Name = "LD",
                        IsAndroidStarted = true,
                    },
                    new() { Index = 1, Name = "LD-1" },
                ]
            )
        );

    [Fact]
    public void SameUidOnTwoInstancesHasSeparateBinding() =>
        Assert.NotEqual(Binding().AccountKey, Binding(2).AccountKey);

    [Fact]
    public void DuplicateNamesCannotResolve() =>
        Assert.Equal(
            -1,
            PFlowDeviceRouting.Resolve(
                "same",
                [new() { Index = 1, Name = "same" }, new() { Index = 2, Name = "same" }]
            )
        );

    [Fact]
    public void ExistingNamesResolve() =>
        Assert.Equal(2, PFlowDeviceRouting.Resolve("LD-2", [new() { Index = 2, Name = "custom" }]));

    [Fact]
    public void NewAccountsAreUnselected()
    {
        var a = new PFlowPersonalAccount();
        Assert.False(a.SelectedForDiscovery);
        Assert.False(a.SelectedForPosting);
        Assert.False(a.SelectedForScreenTime);
    }

    [Fact]
    public void CompleteEnumerationRetainsButDisablesMissingPages()
    {
        var a = Account();
        a.Pages.Add(
            new()
            {
                PageId = "10",
                State = PFlowPageAccessState.Available,
                VerifiedRunId = "old",
            }
        );
        PFlowAccountReconciliation.Apply(a, new("123", "new", true, []), DateTimeOffset.UtcNow);
        Assert.Equal(PFlowPageAccessState.NotInConfirmedSwitcher, Assert.Single(a.Pages).State);
        Assert.False(PFlowAccountReconciliation.CanPost(a, "10", "old"));
    }

    [Fact]
    public void IncompleteEnumerationDoesNotClaimRemovalOrAuthorizePriorEvidence()
    {
        var a = Account();
        a.Pages.Add(
            new()
            {
                PageId = "10",
                State = PFlowPageAccessState.Available,
                VerifiedRunId = "old",
            }
        );
        PFlowAccountReconciliation.Apply(a, new("123", "new", false, []), DateTimeOffset.UtcNow);
        Assert.Equal(PFlowPageAccessState.Available, a.Pages[0].State);
        Assert.False(PFlowAccountReconciliation.CanPost(a, "10", "new"));
        Assert.False(PFlowAccountReconciliation.CanPost(a, "10", "old"));
    }

    [Fact]
    public void OwnerReconciliationIsIsolated()
    {
        var a = Account();
        var b = Account();
        b.Binding = Binding(1, "456");
        PFlowAccountReconciliation.Apply(
            a,
            new("123", "run", true, [new("10", "20", "Page", true)]),
            DateTimeOffset.UtcNow
        );
        Assert.True(PFlowAccountReconciliation.CanPost(a, "10", "run"));
        Assert.Empty(b.Pages);
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountReconciliation.Apply(b, new("123", "run", true, []), DateTimeOffset.UtcNow)
        );
    }

    [Fact]
    public void DuplicatePageNamesDoNotMergeIds()
    {
        var a = Account();
        PFlowAccountReconciliation.Apply(
            a,
            new(
                "123",
                "run",
                true,
                [new("10", "20", "same", true), new("11", "21", "same", false)]
            ),
            DateTimeOffset.UtcNow
        );
        Assert.Equal(2, a.Pages.Count);
        Assert.False(PFlowAccountReconciliation.CanPost(a, "11", "run"));
    }

    [Fact]
    public void MultipleOwnersDoNotChangePublicationDedupe()
    {
        var a = new PFlowJob
        {
            TargetId = "10",
            OwnerUid = "123",
            Sha256 = "hash",
            AccountBinding = Binding(),
        };
        var b = new PFlowJob
        {
            TargetId = "10",
            OwnerUid = "456",
            Sha256 = "hash",
            AccountBinding = Binding(1, "456"),
        };
        Assert.Equal(a.DuplicateKey, b.DuplicateKey);
    }

    [Fact]
    public async Task StorePersistsSelectionsButInvalidatesLiveProofOnRestart()
    {
        var store = new PFlowAccountStore(Path.Combine(directory, "accounts.json"));
        await store.UpdateAsync(accounts =>
        {
            var a = Account();
            a.SelectedForScreenTime = true;
            a.Pages.Add(
                new()
                {
                    PageId = "10",
                    State = PFlowPageAccessState.Available,
                    VerifiedRunId = "run",
                }
            );
            accounts.Add(a);
        });
        var restored = Assert.Single(await new PFlowAccountStore(store.FilePath).LoadAsync());
        Assert.True(restored.SelectedForScreenTime);
        Assert.Equal(PFlowAuthenticationState.Cached, restored.Authentication);
        Assert.False(PFlowAccountReconciliation.CanPost(restored, "10", "run"));
    }

    [Fact]
    public async Task StoreRecoversBackupAndRefusesOverwritingTwoDamagedFiles()
    {
        var store = new PFlowAccountStore(Path.Combine(directory, "accounts.json"));
        await store.UpdateAsync(a => a.Add(Account()));
        await File.WriteAllTextAsync(store.FilePath, "broken");
        Assert.Single(await store.LoadAsync());
        await store.UpdateAsync(a => a[0].Name = "restored");
        await File.WriteAllTextAsync(store.FilePath, "broken");
        await File.WriteAllTextAsync(store.FilePath + ".bak", "also broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateAsync(a => a.Clear()));
        Assert.Equal("broken", await File.ReadAllTextAsync(store.FilePath));
    }

    [Fact]
    public async Task ConcurrentUpdatesRetainDifferentAccounts()
    {
        var path = Path.Combine(directory, "accounts.json");
        await Task.WhenAll(
            Enumerable
                .Range(1, 4)
                .Select(i =>
                    new PFlowAccountStore(path).UpdateAsync(a =>
                        a.Add(new() { Binding = Binding(i) })
                    )
                )
        );
        Assert.Equal(4, (await new PFlowAccountStore(path).LoadAsync()).Count);
    }

    [Fact]
    public async Task CancelledStoreUpdateRetainsExistingData()
    {
        var store = new PFlowAccountStore(Path.Combine(directory, "accounts.json"));
        await store.UpdateAsync(a => a.Add(Account()));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.UpdateAsync(a => a.Clear(), cancelled.Token)
        );
        Assert.Single(await store.LoadAsync());
    }

    private string CreateInstanceFixture(int index)
    {
        Directory.CreateDirectory(Path.Combine(directory, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(directory, "vms", $"leidian{index}"));
        File.WriteAllText(
            Path.Combine(directory, "vms", "config", $"leidian{index}.config"),
            "{\"propertySettings.phoneAndroidId\":\"fixture\"}"
        );
        File.WriteAllText(
            Path.Combine(directory, "vms", $"leidian{index}", "data.vmdk"),
            "mock disk"
        );
        return PFlowDeviceRouting.ReadIdentity(directory, index);
    }

    [Fact]
    public async Task FreshPcRootRowsCreateRenamedLdMappingWithoutClaimingLiveAuthentication()
    {
        CreateInstanceFixture(2);
        var result = PFlowAccountStore.CollectLegacy(
            [
                new()
                {
                    DeviceName = "Atlmobile Mechanic",
                    AppClone = "FB-01",
                    Type = "Profile",
                    PageId = "123",
                    UserUid = "123",
                    PageName = "Bryan Cowan",
                },
                new()
                {
                    DeviceName = "Atlmobile Mechanic",
                    AppClone = "FB-01",
                    Type = "Page",
                    PageId = "456",
                    UserUid = "123",
                    PageName = "Example Page",
                },
            ],
            [new() { Index = 2, Name = "Atlmobile Mechanic" }],
            directory
        );
        Assert.Empty(result.Issues);
        var account = Assert.Single(result.Accounts);
        Assert.Equal(2, account.Binding.DeviceIndex);
        Assert.Equal("Bryan Cowan", account.Name);
        Assert.NotEqual(PFlowAuthenticationState.Authenticated, account.Authentication);
        Assert.Single(account.Pages);
        var store = new PFlowAccountStore(Path.Combine(directory, "fresh-accounts.json"));
        await store.UpdateAsync(accounts => accounts.Add(account));
        Assert.Equal(account.Binding, Assert.Single(await store.LoadAsync()).Binding);
    }

    [Fact]
    public void LegacyMigrationRetainsPageSelectionMediaCaptionAndDates()
    {
        CreateInstanceFixture(1);
        var profile = new FacebookPageTarget
        {
            DeviceName = "LD-1",
            Type = "Profile",
            PageId = "123",
            UserUid = "123",
            IsSelected = false,
        };
        var page = new FacebookPageTarget
        {
            DeviceName = "LD-1",
            PageId = "10",
            UserUid = "123",
            VideoReelPath = "media",
            UseCaptionFromFilename = true,
            CreationDate = "January 2020",
            IsSelected = true,
        };
        var migrated = Assert.Single(
            PFlowAccountStore.MigrateLegacy(
                [profile, page],
                [new() { Index = 1, Name = "LD-1" }],
                directory
            )
        );
        Assert.True(migrated.SelectedForPosting);
        Assert.True(migrated.SelectedForScreenTime);
        Assert.Equal(profile.AccountBinding, page.AccountBinding);
        Assert.True(page.IsSelected);
        Assert.Equal("media", page.VideoReelPath);
        Assert.Equal("January 2020", page.CreationDate);
        Assert.True(page.UseCaptionFromFilename);
    }

    [Fact]
    public void AmbiguousMultipleProfilesAreNeverImplicitlySelected()
    {
        CreateInstanceFixture(1);
        var accounts = PFlowAccountStore.MigrateLegacy(
            [
                new()
                {
                    DeviceName = "LD-1",
                    Type = "Profile",
                    PageId = "123",
                    UserUid = "123",
                },
                new()
                {
                    DeviceName = "LD-1",
                    Type = "Profile",
                    PageId = "456",
                    UserUid = "456",
                },
            ],
            [new() { Index = 1, Name = "LD-1" }],
            directory
        );
        Assert.Equal(2, accounts.Count);
        Assert.All(
            accounts,
            a =>
            {
                Assert.False(a.SelectedForDiscovery);
                Assert.False(a.SelectedForPosting);
                Assert.False(a.SelectedForScreenTime);
            }
        );
    }

    [Fact]
    public void ChangedInstanceFingerprintRejectsSavedMapping()
    {
        var identity = CreateInstanceFixture(1);
        File.WriteAllText(
            Path.Combine(directory, "vms", "config", "leidian1.config"),
            "{\"propertySettings.phoneAndroidId\":\"replacement\"}"
        );
        Assert.Throws<InvalidOperationException>(() =>
            new PFlowAccountBinding(
                identity,
                1,
                "LD-1",
                "com.facebook.katana",
                "123"
            ).RequireWorker(
                1,
                "LD-1",
                PFlowDeviceRouting.ReadIdentity(directory, 1),
                "com.facebook.katana"
            )
        );
    }

    [Fact]
    public void CachedMergeRetainsConfigurationAndRejectsOtherOwner()
    {
        var saved = new FacebookPageTarget
        {
            DeviceName = "LD-1",
            PageId = "10",
            UserUid = "123",
            PageName = "Name",
            VideoReelPath = "media",
            UseCaptionFromFilename = true,
            Birthdate = "March 1999",
            CreationDate = "January 2020",
            Followers = 15,
            AccountBinding = Binding(),
        };
        var incoming = new FacebookPageTarget
        {
            DeviceName = "LD-1",
            PageId = "10",
            UserUid = "123",
            IsSelected = false,
        };
        var result = PFlowTargetMerge.Cached(saved, incoming);
        Assert.Equal("media", result.VideoReelPath);
        Assert.Equal("January 2020", result.CreationDate);
        Assert.Equal(15, result.Followers);
        Assert.True(result.IsSelected);
        Assert.True(result.UseCaptionFromFilename);
        Assert.Equal(saved.AccountBinding, result.AccountBinding);
        incoming.UserUid = "456";
        Assert.Throws<InvalidOperationException>(() => PFlowTargetMerge.Cached(saved, incoming));
    }

    [Fact]
    public async Task BindingRoundTripsWithMediaConfiguration()
    {
        var path = Path.Combine(directory, "pages.json");
        await PFlowPageStorage.SavePagesAsync(
            [
                new()
                {
                    AccountBinding = Binding(),
                    VideoReelPath = "media",
                    UseTitleFromTextFile = true,
                    CreationDate = "date",
                },
            ],
            path
        );
        var row = Assert.Single(await PFlowPageStorage.LoadPagesAsync(path));
        Assert.Equal(Binding(), row.AccountBinding);
        Assert.Equal("media", row.VideoReelPath);
        Assert.True(row.UseTitleFromTextFile);
        Assert.Equal("date", row.CreationDate);
    }

    [Theory]
    [InlineData("10", "", true)]
    [InlineData("10", "persona", true)]
    [InlineData("not-id", "20", false)]
    public void InvalidLiveIdsDoNotPartiallyChangeAccess(
        string pageId,
        string persona,
        bool verified
    )
    {
        var a = Account();
        a.Pages.Add(
            new()
            {
                PageId = "99",
                Name = "retained",
                VerifiedRunId = "old",
            }
        );
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountReconciliation.Apply(
                a,
                new(
                    "123",
                    "run",
                    true,
                    [new("11", "21", "first", true), new(pageId, persona, "bad", verified)]
                ),
                DateTimeOffset.UtcNow
            )
        );
        Assert.Equal("retained", Assert.Single(a.Pages).Name);
        Assert.Equal("old", a.Pages[0].VerifiedRunId);
    }

    [Fact]
    public void DuplicateCachedAccessCannotPartiallyReconcileOrAuthorize()
    {
        var a = Account();
        a.Pages.Add(
            new()
            {
                PageId = "10",
                PersonaUid = "20",
                State = PFlowPageAccessState.Available,
                VerifiedRunId = "run",
            }
        );
        a.Pages.Add(new() { PageId = "10" });
        Assert.False(PFlowAccountReconciliation.CanPost(a, "10", "run"));
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountReconciliation.Apply(
                a,
                new("123", "new", true, [new("11", "21", "new", true)]),
                DateTimeOffset.UtcNow
            )
        );
        Assert.Equal(2, a.Pages.Count);
    }

    [Fact]
    public async Task FutureStoreVersionCannotFallBackToOldBackupOrBeOverwritten()
    {
        var store = new PFlowAccountStore(Path.Combine(directory, "accounts.json"));
        await store.UpdateAsync(a => a.Add(Account()));
        var future = "{\"Version\":2,\"Accounts\":[]}";
        await File.WriteAllTextAsync(store.FilePath, future);
        await Assert.ThrowsAsync<PFlowAccountVersionException>(() => store.LoadAsync());
        await Assert.ThrowsAsync<PFlowAccountVersionException>(() =>
            store.UpdateAsync(a => a.Clear())
        );
        Assert.Equal(future, await File.ReadAllTextAsync(store.FilePath));
    }

    [Fact]
    public async Task InvalidStoredBindingRecoversGoodBackup()
    {
        var store = new PFlowAccountStore(Path.Combine(directory, "accounts.json"));
        await store.UpdateAsync(a => a.Add(Account()));
        var text = await File.ReadAllTextAsync(store.FilePath);
        await File.WriteAllTextAsync(
            store.FilePath,
            text.Replace("\"OwnerUid\": \"123\"", "\"OwnerUid\": \"invalid\"")
        );
        Assert.Equal("123", Assert.Single(await store.LoadAsync()).Binding.OwnerUid);
    }

    [Fact]
    public void LegacyMigrationFailureDoesNotMutateEarlierProfile()
    {
        CreateInstanceFixture(1);
        CreateInstanceFixture(2);
        var first = new FacebookPageTarget
        {
            DeviceName = "LD-1",
            Type = "Profile",
            PageId = "123",
            UserUid = "123",
        };
        var bad = new FacebookPageTarget
        {
            DeviceName = "LD-2",
            Type = "Profile",
            PageId = "456",
            UserUid = "456",
            AccountBinding = Binding(2, "456"),
        };
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountStore.MigrateLegacy(
                [first, bad],
                [new() { Index = 1, Name = "LD-1" }, new() { Index = 2, Name = "LD-2" }],
                directory
            )
        );
        Assert.Null(first.AccountBinding);
    }

    [Fact]
    public void StaleMappingDoesNotPreventHealthyCandidateOrMutateSourceRows()
    {
        CreateInstanceFixture(1);
        CreateInstanceFixture(2);
        var good = new FacebookPageTarget
        {
            DeviceName = "LD-1",
            Type = "Profile",
            PageId = "123",
            UserUid = "123",
        };
        var bad = new FacebookPageTarget
        {
            DeviceName = "LD-2",
            Type = "Profile",
            PageId = "456",
            UserUid = "456",
            AccountBinding = Binding(2, "456"),
        };
        var result = PFlowAccountStore.CollectLegacy(
            [good, bad],
            [new() { Index = 1, Name = "LD-1" }, new() { Index = 2, Name = "LD-2" }],
            directory
        );
        Assert.Equal("123", Assert.Single(result.Accounts).Binding.OwnerUid);
        Assert.Equal("LD-2", Assert.Single(result.Issues).DeviceName);
        Assert.Null(good.AccountBinding);
        Assert.Equal(Binding(2, "456"), bad.AccountBinding);
    }

    [Fact]
    public void CollectedMultipleOwnersRemainOptIn()
    {
        CreateInstanceFixture(1);
        var result = PFlowAccountStore.CollectLegacy(
            [
                new()
                {
                    DeviceName = "LD-1",
                    Type = "Profile",
                    PageId = "123",
                    UserUid = "123",
                },
                new()
                {
                    DeviceName = "LD-1",
                    Type = "Profile",
                    PageId = "456",
                    UserUid = "456",
                },
            ],
            [new() { Index = 1, Name = "LD-1" }],
            directory
        );
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Accounts.Count);
        Assert.All(
            result.Accounts,
            a =>
            {
                Assert.False(a.SelectedForDiscovery);
                Assert.False(a.SelectedForScreenTime);
                Assert.False(a.SelectedForPosting);
            }
        );
    }

    [Fact]
    public void CurrentBindingRejectsRenamedOrReplacedInstance()
    {
        var identity = CreateInstanceFixture(1);
        var binding = new PFlowAccountBinding(identity, 1, "LD-1", "com.facebook.katana", "123");
        PFlowAccountStore.RequireCurrentBinding(
            binding,
            [new() { Index = 1, Name = "LD-1" }],
            directory
        );
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountStore.RequireCurrentBinding(
                binding,
                [new() { Index = 1, Name = "renamed" }],
                directory
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountStore.RequireCurrentBinding(binding, [], directory)
        );
    }

    [Fact]
    public async Task PreCancelledSessionInvalidatesPriorAuthenticationAndPageAccess()
    {
        var account = Account();
        account.Pages.Add(
            new()
            {
                PageId = "10",
                PersonaUid = "20",
                State = PFlowPageAccessState.Available,
                VerifiedRunId = "old",
            }
        );
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowAccountSessionService(new Navigator()).EnsurePersonalAsync(
                account,
                ct.Token,
                true
            )
        );
        Assert.NotEqual(PFlowAuthenticationState.Authenticated, account.Authentication);
        Assert.Empty(account.Pages[0].VerifiedRunId);
        Assert.False(PFlowAccountReconciliation.CanPost(account, "10", "old"));
    }

    sealed class Navigator : IPFlowAccountSessionNavigator
    {
        public int Switches;
        public int WorkerReads;
        public string LiveUid = "123";
        public bool Live = true;
        public bool Personal = true;
        public bool WrongWorker;
        public PFlowAuthenticationState State = PFlowAuthenticationState.Authenticated;

        public Task<PFlowWorkerIdentity> ReadWorkerAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            WorkerReads++;
            return Task.FromResult(
                new PFlowWorkerIdentity(
                    WrongWorker ? 2 : b.DeviceIndex,
                    b.DeviceName,
                    b.InstanceIdentity,
                    b.AppPackage
                )
            );
        }

        public Task<PFlowAccountObservation> ObserveAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(
                new PFlowAccountObservation(
                    LiveUid,
                    b.AppPackage,
                    Personal,
                    true,
                    Live,
                    DateTimeOffset.UtcNow,
                    State
                )
            );
        }

        public Task<bool> SwitchSavedPersonalAccountAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Switches++;
            LiveUid = b.OwnerUid;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task ReusesOnlyLivePersonalSession()
    {
        var n = new Navigator();
        Assert.True(
            await new PFlowAccountSessionService(n).EnsurePersonalAsync(Account(), default, true)
        );
        Assert.Equal(0, n.Switches);
        Assert.Equal(2, n.WorkerReads);
    }

    [Fact]
    public async Task SwitchesOwnerAndObservesAgain()
    {
        var n = new Navigator { LiveUid = "456" };
        Assert.True(
            await new PFlowAccountSessionService(n).EnsurePersonalAsync(Account(), default, true)
        );
        Assert.Equal(1, n.Switches);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CachedUidOrPageSessionCannotAuthenticate(bool live, bool personal)
    {
        var n = new Navigator { Live = live, Personal = personal };
        var a = Account();
        Assert.False(await new PFlowAccountSessionService(n).EnsurePersonalAsync(a, default, true));
        Assert.Equal(PFlowAuthenticationState.VerificationFailed, a.Authentication);
        Assert.Equal(1, n.Switches);
    }

    [Theory]
    [InlineData(PFlowAuthenticationState.Checkpoint)]
    [InlineData(PFlowAuthenticationState.LoginRequired)]
    public async Task UserChallengesDoNotAttemptAccountSwitch(PFlowAuthenticationState state)
    {
        var n = new Navigator { State = state };
        var a = Account();
        Assert.False(await new PFlowAccountSessionService(n).EnsurePersonalAsync(a, default, true));
        Assert.Equal(0, n.Switches);
        Assert.Equal(state, a.Authentication);
    }

    [Fact]
    public async Task WorkerMismatchFailsBeforeAnyObservationOrSwitch()
    {
        var n = new Navigator { WrongWorker = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowAccountSessionService(n).EnsurePersonalAsync(Account(), default, true)
        );
        Assert.Equal(0, n.Switches);
    }

    [Fact]
    public async Task CancellationNeverAuthenticates()
    {
        var n = new Navigator();
        var a = Account();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowAccountSessionService(n).EnsurePersonalAsync(a, ct.Token, true)
        );
        Assert.NotEqual(PFlowAuthenticationState.Authenticated, a.Authentication);
    }
}
