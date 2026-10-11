using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowAccountRemapTests
{
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
    public async Task ExplicitRemapInvalidatesProofsAndCredentialsAndSelections()
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
        Assert.Equal("", account.CredentialReference);
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

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "pflow-remap-" + Guid.NewGuid().ToString("N"));
        public PFlowAccountBinding Old { get; } =
            new("old-fingerprint", 8, "Old instance", "com.facebook.katana", "123");
        public PFlowAccountBinding Next { get; }
        public LDInstanceInfo[] Instances { get; } = [new() { Index = 8, Name = "New instance" }];
        public PFlowAccountStore Store { get; }

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
            Store = new(Path.Combine(Root, "accounts.json"));
        }

        public Task Seed() =>
            Store.UpdateAsync(a =>
                a.Add(
                    new()
                    {
                        Binding = Old,
                        Name = "Owner",
                        CredentialReference = "old-protected-ref",
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
