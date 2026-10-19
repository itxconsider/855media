using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowAccountRemapTests
{
    private static PFlowAccountBinding Destination(Fixture f)
    {
        Directory.CreateDirectory(Path.Combine(f.Root, "vms/leidian9"));
        File.WriteAllText(
            Path.Combine(f.Root, "vms/config/leidian9.config"),
            "{\"propertySettings.phoneAndroidId\":\"destination\"}"
        );
        File.WriteAllText(Path.Combine(f.Root, "vms/leidian9/data.vmdk"), "fixture");
        return f.Old with
        {
            DeviceIndex = 9,
            DeviceName = "LD-9",
            InstanceIdentity = PFlowDeviceRouting.ReadIdentity(f.Root, 9),
        };
    }

    [Fact]
    public async Task ExplicitTransferRetainsAccountCredentialsAndInvalidatesRunProofs()
    {
        using var f = new Fixture();
        await f.Seed();
        var next = Destination(f);
        Assert.Equal("old-protected-ref", await f.Store.TransferAsync(f.Old, next));
        var saved = (await f.Store.LoadAsync()).Single();
        Assert.Equal(next, saved.Binding);
        Assert.Equal(new[] { f.Old }, saved.TransferredFromBindings);
        Assert.Empty(saved.PreviousBindings);
        Assert.Equal(f.Credentials.LastReference, saved.CredentialReference);
        Assert.False(saved.SelectedForDiscovery);
        Assert.False(saved.SelectedForScreenTime);
        Assert.False(saved.SelectedForPosting);
        Assert.Equal("456", saved.Pages.Single().PageId);
        Assert.Equal(PFlowPageAccessState.Cached, saved.Pages.Single().State);
        Assert.Null(saved.LastVerifiedUtc);
    }

    [Fact]
    public async Task TransferJournalRecoversBoundAndLegacyTargetsAfterRestart()
    {
        using var f = new Fixture();
        await f.Seed();
        var next = Destination(f);
        await f.Store.TransferAsync(f.Old, next);
        var rows = new[]
        {
            new FacebookPageTarget
            {
                DeviceName = f.Old.DeviceName,
                UserUid = f.Old.OwnerUid,
                PageId = "456",
                AppClone = "FB-01",
                AccountBinding = f.Old,
                VideoReelPath = "D:/media",
                IsSelected = true,
                LivePageAccessVerified = true,
            },
            new FacebookPageTarget
            {
                DeviceName = f.Old.DeviceName,
                UserUid = f.Old.OwnerUid,
                PageId = f.Old.OwnerUid,
                AppClone = "FB-01",
                Type = "Profile",
            },
        };
        var reopened = new PFlowAccountStore(f.Store.FilePath, f.Credentials);
        Assert.Equal(
            2,
            PFlowAccountRemap.ReplayRows(
                await reopened.LoadAsync(),
                rows,
                f.Instances.Append(new LDInstanceInfo { Index = 9, Name = "LD-9" }).ToArray(),
                f.Root
            )
        );
        Assert.All(
            rows,
            r =>
            {
                Assert.Equal(next, r.AccountBinding);
                Assert.Equal("LD-9", r.DeviceName);
                Assert.False(r.IsSelected);
                Assert.False(r.LivePageAccessVerified);
            }
        );
        Assert.Equal("D:/media", rows[0].VideoReelPath);
        Assert.Equal(
            0,
            PFlowAccountRemap.ReplayRows(
                await reopened.LoadAsync(),
                rows,
                f.Instances.Append(new LDInstanceInfo { Index = 9, Name = "LD-9" }).ToArray(),
                f.Root
            )
        );
    }

    [Fact]
    public async Task TransferToExistingDestinationDoesNotOverwriteEitherAccount()
    {
        using var f = new Fixture();
        await f.Seed();
        var next = Destination(f);
        await f.Store.UpdateAsync(a => a.Add(new() { Binding = next, Name = "Existing" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Store.TransferAsync(f.Old, next)
        );
        Assert.Equal(2, (await f.Store.LoadAsync()).Count);
        Assert.Equal(
            "old-protected-ref",
            (await f.Store.LoadAsync()).Single(a => a.Binding == f.Old).CredentialReference
        );
        Assert.Contains(f.Credentials.LastReference, f.Credentials.Deleted);
    }

    [Fact]
    public async Task RemovalPersistsExclusionsAndExplicitReaddCanRestoreMapping()
    {
        using var f = new Fixture();
        await f.Seed();
        var next = Destination(f);
        await f.Store.TransferAsync(f.Old, next);
        Assert.Equal(f.Credentials.LastReference, await f.Store.RemoveAsync(next));
        var reopened = new PFlowAccountStore(f.Store.FilePath, f.Credentials);
        Assert.Empty(await reopened.LoadAsync());
        Assert.Contains(f.Old, await reopened.RemovedBindingsAsync());
        Assert.Contains(next, await reopened.RemovedBindingsAsync());
        await reopened.UpdateAsync(a => a.Add(new() { Binding = next }));
        Assert.DoesNotContain(next, await reopened.RemovedBindingsAsync());
        Assert.Single(await reopened.LoadAsync());
    }

    [Fact]
    public async Task TransferCannotChangeUidOrAppAndStaleRemovalCannotDeleteReplacement()
    {
        using var f = new Fixture();
        await f.Seed();
        var next = Destination(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Store.TransferAsync(f.Old, next with { OwnerUid = "999" })
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Store.TransferAsync(f.Old, next with { AppPackage = "other.app" })
        );
        await f.Store.TransferAsync(f.Old, next);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.RemoveAsync(f.Old));
        Assert.Equal(next, (await f.Store.LoadAsync()).Single().Binding);
    }

    [Fact]
    public async Task RestartReroutesOnlyPendingQueueRowsAndRemovalKeepsHistoricalRows()
    {
        using var f = new Fixture();
        await f.Seed();
        var next = Destination(f);
        var pending = new AutoPostTaskItem
        {
            AccountBinding = f.Old,
            DeviceName = f.Old.DeviceName,
            OwnerUid = f.Old.OwnerUid,
            PFlowJobId = "old-reservation",
            Status = "Queued",
            FilePath = "D:/media/reel.mp4",
        };
        var finished = new AutoPostTaskItem
        {
            AccountBinding = f.Old,
            DeviceName = f.Old.DeviceName,
            Status = "Confirmed",
        };
        var other = new AutoPostTaskItem
        {
            DeviceName = f.Old.DeviceName,
            OwnerUid = "999",
            Status = "Queued",
        };
        var rows = new List<AutoPostTaskItem> { pending, finished, other };
        await f.Store.TransferAsync(f.Old, next);
        Assert.Equal(1, PFlowAccountRemap.ReplayQueuedTasks(await f.Store.LoadAsync(), [], rows));
        Assert.Equal(next, pending.AccountBinding);
        Assert.Equal(next.DeviceName, pending.DeviceName);
        Assert.Equal("", pending.PFlowJobId);
        Assert.Equal("D:/media/reel.mp4", pending.FilePath);
        Assert.Equal(f.Old, finished.AccountBinding);
        Assert.Equal(0, PFlowAccountRemap.ReplayQueuedTasks(await f.Store.LoadAsync(), [], rows));
        await f.Store.RemoveAsync(next);
        Assert.Equal(
            1,
            PFlowAccountRemap.ReplayQueuedTasks([], await f.Store.RemovedBindingsAsync(), rows)
        );
        Assert.DoesNotContain(pending, rows);
        Assert.Contains(finished, rows);
        Assert.Contains(other, rows);
    }

    [Theory]
    [InlineData("index")]
    [InlineData("package")]
    [InlineData("uid")]
    public async Task CannotMoveAnAccountOutsideItsAssignment(string change)
    {
        using var f = new Fixture();
        await f.Seed();
        var next =
            change == "index" ? f.Next with { DeviceIndex = 9 }
            : change == "package" ? f.Next with { AppPackage = "other.app" }
            : f.Next with { OwnerUid = "999" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.RemapAsync(f.Old, next));
        Assert.Equal(f.Old, (await f.Store.LoadAsync()).Single().Binding);
    }

    [Fact]
    public async Task ExplicitRemapRetainsCredentialsButInvalidatesProofsAndSelections()
    {
        using var f = new Fixture();
        await f.Seed();
        Assert.Equal("old-protected-ref", await f.Store.RemapAsync(f.Old, f.Next));
        var account = (await f.Store.LoadAsync()).Single();
        Assert.Equal(f.Next, account.Binding);
        Assert.Equal(new[] { f.Old }, account.PreviousBindings);
        Assert.False(account.SelectedForDiscovery);
        Assert.False(account.SelectedForScreenTime);
        Assert.False(account.SelectedForPosting);
        Assert.Equal(f.Credentials.LastReference, account.CredentialReference);
        Assert.True(account.AllowAutomaticLogin);
        Assert.True(account.AllowAccountRotation);
        Assert.Null(account.LastVerifiedUtc);
        Assert.Equal(PFlowAuthenticationState.Cached, account.Authentication);
        Assert.Equal(PFlowPageAccessState.Cached, account.Pages.Single().State);
        Assert.Null(account.Pages.Single().LastVerifiedUtc);
        Assert.Equal("", account.Pages.Single().VerifiedRunId);
        Assert.Equal("Page", account.Pages.Single().Name);
    }

    [Fact]
    public async Task StaleReviewCannotOverwriteAnotherRemap()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Store.RemapAsync(f.Old, f.Next);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Store.RemapAsync(f.Old, f.Next with { DeviceName = "Other" })
        );
        Assert.Equal(f.Next, (await f.Store.LoadAsync()).Single().Binding);
    }

    [Fact]
    public async Task JournalRecoversOldPageRowsAfterRestartWithoutChangingPathsOrHistory()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Store.RemapAsync(f.Old, f.Next);
        var rows = new[]
        {
            new FacebookPageTarget
            {
                DeviceName = f.Old.DeviceName,
                UserUid = "123",
                PageId = "456",
                AppClone = "FB-01",
                AccountBinding = f.Old,
                VideoReelPath = "D:/media",
                TitleVideoReel = "Caption",
            },
            new FacebookPageTarget
            {
                DeviceName = f.Old.DeviceName,
                UserUid = "123",
                PageId = "123",
                AppClone = "FB-01",
                Type = "Profile",
            },
        };
        var job = new PFlowJob { AccountBinding = f.Old, DeviceIndex = 8 };
        var accounts = await new PFlowAccountStore(f.Store.FilePath).LoadAsync();
        Assert.Equal(2, PFlowAccountRemap.ReplayRows(accounts, rows, f.Instances, f.Root));
        Assert.All(
            rows,
            r =>
            {
                Assert.Equal(f.Next, r.AccountBinding);
                Assert.Equal("New instance", r.DeviceName);
            }
        );
        Assert.Equal("D:/media", rows[0].VideoReelPath);
        Assert.Equal("Caption", rows[0].TitleVideoReel);
        Assert.Equal(f.Old, job.AccountBinding);
        Assert.Equal(0, PFlowAccountRemap.ReplayRows(accounts, rows, f.Instances, f.Root));
    }

    [Fact]
    public async Task ReplayNeverUsesAChangedCurrentFingerprintOrOtherOwner()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Store.RemapAsync(f.Old, f.Next);
        var row = new FacebookPageTarget
        {
            DeviceName = f.Old.DeviceName,
            UserUid = "999",
            AccountBinding = f.Old,
            AppClone = "FB-01",
        };
        var accounts = await f.Store.LoadAsync();
        Assert.Equal(0, PFlowAccountRemap.ReplayRows(accounts, new[] { row }, f.Instances, f.Root));
        row.UserUid = "123";
        File.WriteAllText(
            Path.Combine(f.Root, "vms/config/leidian8.config"),
            "{\"propertySettings.phoneAndroidId\":\"changed-again\"}"
        );
        Assert.Equal(0, PFlowAccountRemap.ReplayRows(accounts, new[] { row }, f.Instances, f.Root));
        Assert.Equal(f.Old, row.AccountBinding);
    }

    [Fact]
    public async Task LegacyNameReusedByAnotherIndexIsRejectedWithoutPartialChanges()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Store.RemapAsync(f.Old, f.Next);
        var rows = new[]
        {
            new FacebookPageTarget
            {
                UserUid = "123",
                AppClone = "FB-01",
                DeviceName = f.Old.DeviceName,
                AccountBinding = f.Old,
            },
            new FacebookPageTarget
            {
                UserUid = "123",
                AppClone = "FB-01",
                DeviceName = f.Old.DeviceName,
            },
        };
        var accounts = await f.Store.LoadAsync();
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountRemap.ReplayRows(
                accounts,
                rows,
                f.Instances.Append(new LDInstanceInfo { Index = 9, Name = f.Old.DeviceName })
                    .ToArray(),
                f.Root
            )
        );
        Assert.Equal(f.Old, rows[0].AccountBinding);
        Assert.Null(rows[1].AccountBinding);
    }

    [Fact]
    public async Task RevertingToPreviousBindingKeepsUnambiguousJournal()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Store.RemapAsync(f.Old, f.Next);
        await f.Store.RemapAsync(f.Next, f.Old);
        var account = (await f.Store.LoadAsync()).Single();
        Assert.Equal(new[] { f.Next }, account.PreviousBindings);
    }

    [Fact]
    public async Task RefreshIsolatesBadRemapRowsFromHealthyAccounts()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Store.RemapAsync(f.Old, f.Next);
        Directory.CreateDirectory(Path.Combine(f.Root, "vms/leidian9"));
        File.WriteAllText(
            Path.Combine(f.Root, "vms/config/leidian9.config"),
            "{\"propertySettings.phoneAndroidId\":\"healthy\"}"
        );
        File.WriteAllText(Path.Combine(f.Root, "vms/leidian9/data.vmdk"), "fixture");
        var oldHealthy = new PFlowAccountBinding(
            "old-healthy",
            9,
            "Old healthy",
            "com.facebook.katana",
            "222"
        );
        var healthy = oldHealthy with
        {
            InstanceIdentity = PFlowDeviceRouting.ReadIdentity(f.Root, 9),
            DeviceName = f.Old.DeviceName,
        };
        var accounts = await f.Store.LoadAsync();
        accounts.Add(new() { Binding = healthy, PreviousBindings = [oldHealthy] });
        var bad = new FacebookPageTarget
        {
            UserUid = "123",
            DeviceName = f.Old.DeviceName,
            AppClone = "FB-01",
        };
        var good = new FacebookPageTarget
        {
            UserUid = "222",
            DeviceName = "Old healthy",
            AccountBinding = oldHealthy,
            AppClone = "FB-01",
        };
        var issues = new System.Collections.Generic.List<PFlowMappingIssue>();
        Assert.Equal(
            1,
            PFlowAccountRemap.ReplayRows(
                accounts,
                new[] { bad, good },
                f.Instances.Append(new LDInstanceInfo { Index = 9, Name = healthy.DeviceName })
                    .ToArray(),
                f.Root,
                issues.Add
            )
        );
        Assert.Null(bad.AccountBinding);
        Assert.Equal(healthy, good.AccountBinding);
        Assert.Equal("123", Assert.Single(issues).OwnerUid);
    }

    [Fact]
    public async Task UnreadableCredentialStopsRemapWithoutDeletingTheStoredRecord()
    {
        using var f = new Fixture();
        await f.Seed();
        f.Credentials.FailLoad = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Store.RemapAsync(f.Old, f.Next)
        );
        var account = (await f.Store.LoadAsync()).Single();
        Assert.Equal(f.Old, account.Binding);
        Assert.Equal("old-protected-ref", account.CredentialReference);
        Assert.Empty(f.Credentials.Deleted);
    }

    [Fact]
    public async Task ConcurrentCredentialEditRollsBackOnlyTheNewProtectedCopy()
    {
        using var f = new Fixture();
        await f.Seed();
        f.Credentials.OnSave = () =>
            f.Store.UpdateAsync(a => a.Single().CredentialReference = "new-user-edit");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Store.RemapAsync(f.Old, f.Next)
        );
        var account = (await f.Store.LoadAsync()).Single();
        Assert.Equal(f.Old, account.Binding);
        Assert.Equal("new-user-edit", account.CredentialReference);
        Assert.Equal(new[] { f.Credentials.LastReference }, f.Credentials.Deleted);
    }

    [Fact]
    public async Task RemapWithoutCredentialsKeepsLoginPermissionsDisabled()
    {
        using var f = new Fixture();
        await f.Seed();
        await f.Store.UpdateAsync(a => a.Single().CredentialReference = "");
        await f.Store.RemapAsync(f.Old, f.Next);
        var account = (await f.Store.LoadAsync()).Single();
        Assert.Equal("", account.CredentialReference);
        Assert.False(account.AllowAutomaticLogin);
        Assert.False(account.AllowAccountRotation);
        Assert.Equal(0, f.Credentials.Saves);
    }

    private sealed class FakeCredentials : IPFlowCredentialStore
    {
        public bool FailLoad;
        public int Saves;
        public string LastReference = "";
        public List<string> Deleted = [];
        public Func<Task>? OnSave;

        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding binding,
            string reference,
            CancellationToken ct = default
        )
        {
            if (FailLoad)
                throw new InvalidOperationException("Fixture credential could not be opened.");
            return Task.FromResult(
                PFlowCredentialImport.Parse(
                    binding.OwnerUid + "|fixture-password||",
                    binding,
                    "fixture-login",
                    PFlowTwoFactorKind.None
                )
            );
        }

        public async Task<string> SaveAsync(
            PFlowAccountBinding binding,
            PFlowCredential credential,
            CancellationToken ct = default
        )
        {
            LastReference = "retained-protected-ref-" + ++Saves;
            if (OnSave != null)
                await OnSave();
            return LastReference;
        }

        public void Delete(string reference) => Deleted.Add(reference);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "pflow-remap-" + Guid.NewGuid().ToString("N"));
        public PFlowAccountBinding Old { get; } =
            new("old-fingerprint", 8, "Old instance", "com.facebook.katana", "123");
        public PFlowAccountBinding Next { get; }
        public LDInstanceInfo[] Instances { get; } = [new() { Index = 8, Name = "New instance" }];
        public PFlowAccountStore Store { get; }
        public FakeCredentials Credentials { get; } = new();

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "vms/config"));
            Directory.CreateDirectory(Path.Combine(Root, "vms/leidian8"));
            File.WriteAllText(
                Path.Combine(Root, "vms/config/leidian8.config"),
                "{\"propertySettings.phoneAndroidId\":\"fixture\"}"
            );
            File.WriteAllText(Path.Combine(Root, "vms/leidian8/data.vmdk"), "fixture");
            Next = Old with
            {
                InstanceIdentity = PFlowDeviceRouting.ReadIdentity(Root, 8),
                DeviceName = "New instance",
            };
            Store = new(Path.Combine(Root, "accounts.json"), Credentials);
        }

        public Task Seed() =>
            Store.UpdateAsync(a =>
                a.Add(
                    new()
                    {
                        Binding = Old,
                        Name = "Owner",
                        CredentialReference = "old-protected-ref",
                        AllowAutomaticLogin = true,
                        AllowAccountRotation = true,
                        SelectedForDiscovery = true,
                        SelectedForScreenTime = true,
                        SelectedForPosting = true,
                        Authentication = PFlowAuthenticationState.Authenticated,
                        LastVerifiedUtc = DateTimeOffset.UtcNow,
                        Pages =
                        [
                            new()
                            {
                                PageId = "456",
                                Name = "Page",
                                PersonaUid = "789",
                                State = PFlowPageAccessState.Available,
                                VerifiedRunId = "old-run",
                                LastVerifiedUtc = DateTimeOffset.UtcNow,
                            },
                        ],
                    }
                )
            );

        public void Dispose()
        {
            if (
                !Path.GetFullPath(Root)
                    .StartsWith(
                        Path.GetFullPath(Path.GetTempPath()),
                        StringComparison.OrdinalIgnoreCase
                    )
            )
                throw new InvalidOperationException();
            Directory.Delete(Root, true);
        }
    }
}
