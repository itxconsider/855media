using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowInstallationDataTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "855-data-scope-" + Guid.NewGuid().ToString("N")
    );
    private string Legacy => Path.Combine(root, "legacy");

    private PFlowInstallationData Profile(string version) => new(Path.Combine(root, version), root);

    private static PFlowAccountBinding Binding(string identity) =>
        new(identity, 45, "LD-45", "com.facebook.katana", "123");

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Fact]
    public void CanonicalPathsShareDataButVersionsAndInstallationsDoNot()
    {
        var nine = Profile("LDPlayer9");
        var console = new PFlowInstallationData(
            Path.Combine(root, "LDPlayer9", "ldconsole.exe"),
            root
        );
        Assert.Equal(nine.DirectoryPath, console.DirectoryPath);
        Assert.Equal(
            nine.DirectoryPath,
            new PFlowInstallationData(
                Path.Combine(root, "ldplayer9") + Path.DirectorySeparatorChar,
                root
            ).DirectoryPath
        );
        Assert.NotEqual(nine.DirectoryPath, Profile("LDPlayer14").DirectoryPath);
        Assert.NotEqual(nine.DirectoryPath, Profile("another-LDPlayer9").DirectoryPath);
    }

    private async Task SeedAsync()
    {
        Directory.CreateDirectory(Legacy);
        var accounts = new PFlowAccountStore(Path.Combine(Legacy, "pflow_accounts.json"));
        await accounts.UpdateAsync(rows =>
            rows.AddRange([
                new()
                {
                    Binding = Binding("nine"),
                    Name = "Nine",
                    SelectedForPosting = true,
                },
                new()
                {
                    Binding = Binding("fourteen"),
                    Name = "Fourteen",
                    SelectedForPosting = false,
                },
            ])
        );
        await PFlowPageStorage.SavePagesAsync(
            [
                new()
                {
                    AccountBinding = Binding("nine"),
                    PageId = "91",
                    VideoReelPath = "nine-media",
                },
                new()
                {
                    AccountBinding = Binding("fourteen"),
                    PageId = "141",
                    VideoReelPath = "fourteen-media",
                },
                new() { PageId = "legacy" },
            ],
            Path.Combine(Legacy, "facebook_pages.json")
        );
        var queue = new PFlowDispatchQueueStore(Path.Combine(Legacy, "facebook_dispatch_queue.db"));
        foreach (var identity in new[] { "nine", "fourteen" })
        {
            queue.Save(new() { AccountBinding = Binding(identity), Title = identity });
            var history = new PFlowHistoryStore(Path.Combine(Legacy, "facebook_post_history.db"));
            var job = new PFlowJob
            {
                AccountBinding = Binding(identity),
                TargetId = identity,
                Sha256 = identity,
            };
            Assert.True(await history.ReserveAsync(job));
            job.State = PFlowState.Unknown;
            await history.SaveAsync(job);
        }
        queue.Save(new() { Title = "unbound" });
        await new ScreenTimeHistoryStore(Path.Combine(Legacy, "screentime_history.db")).StartAsync(
            "session",
            "123",
            45
        );
        await AutoPostConfigStorage.SaveConfigAsync(
            new() { LdFolder = Path.Combine(root, "LDPlayer9"), RunThread = 3 },
            Path.Combine(Legacy, "autopost_config.json")
        );
        var baselines = new PFlowHistoryStore(Path.Combine(Legacy, "facebook_post_history.db"));
        await baselines.DailyCountAsync("91", 4);
        await baselines.DailyCountAsync("141", 7);
    }

    [Fact]
    public async Task MigrationSeparatesSameIndexAccountsPagesQueuesAndUnknownOutcomes()
    {
        await SeedAsync();
        var source = await File.ReadAllBytesAsync(Path.Combine(Legacy, "pflow_accounts.json"));
        foreach (
            var (version, identity, original) in new[]
            {
                ("LDPlayer9", "nine", true),
                ("LDPlayer14", "fourteen", false),
            }
        )
        {
            var profile = Profile(version);
            await profile.InitializeAsync(new HashSet<string> { identity }, original, Legacy);
            var account = Assert.Single(
                await new PFlowAccountStore(profile.FilePath("pflow_accounts.json")).LoadAsync()
            );
            Assert.Equal(identity, account.Binding.InstanceIdentity);
            Assert.Equal(original, account.SelectedForPosting);
            var pages = await PFlowPageStorage.LoadPagesAsync(
                profile.FilePath("facebook_pages.json")
            );
            Assert.Equal(original ? 2 : 1, pages.Count);
            Assert.DoesNotContain(
                pages,
                p => p.AccountBinding != null && p.AccountBinding.InstanceIdentity != identity
            );
            Assert.Contains(pages, p => p.VideoReelPath == identity + "-media");
            var queue = new PFlowDispatchQueueStore(
                profile.FilePath("facebook_dispatch_queue.db")
            ).Read();
            Assert.Equal(original ? 2 : 1, queue.Count);
            Assert.Contains(queue, q => q.Title == identity);
            var history = Assert.Single(
                await new PFlowHistoryStore(
                    profile.FilePath("facebook_post_history.db")
                ).ReadAsync()
            );
            Assert.Equal(identity, history.AccountBinding!.InstanceIdentity);
            Assert.Equal(PFlowState.Unknown, history.State);
            var counters = new PFlowHistoryStore(profile.FilePath("facebook_post_history.db"));
            Assert.Equal(
                original ? 4 : 7,
                await counters.DailyCountAsync(original ? "91" : "141", 0)
            );
            Assert.Equal(0, await counters.DailyCountAsync(original ? "141" : "91", 0));
            Assert.Equal(original, File.Exists(profile.FilePath("screentime_history.db")));
            Assert.Equal(original, File.Exists(profile.FilePath("autopost_config.json")));
        }
        Assert.Equal(
            source,
            await File.ReadAllBytesAsync(Path.Combine(Legacy, "pflow_accounts.json"))
        );
        Assert.Equal(
            3,
            new PFlowDispatchQueueStore(Path.Combine(Legacy, "facebook_dispatch_queue.db"))
                .Read()
                .Count
        );
    }

    [Fact]
    public async Task ReturningToInstallationPreservesEditsAndDoesNotResurrectRemovedAccounts()
    {
        await SeedAsync();
        var nine = Profile("LDPlayer9");
        var fourteen = Profile("LDPlayer14");
        await nine.InitializeAsync(new HashSet<string> { "nine" }, true, Legacy);
        await fourteen.InitializeAsync(new HashSet<string> { "fourteen" }, false, Legacy);
        await new PFlowAccountStore(nine.FilePath("pflow_accounts.json")).UpdateAsync(a =>
            a.Clear()
        );
        new PFlowDispatchQueueStore(nine.FilePath("facebook_dispatch_queue.db")).Clear();
        await nine.InitializeAsync(new HashSet<string> { "nine" }, true, Legacy);
        Assert.Empty(await new PFlowAccountStore(nine.FilePath("pflow_accounts.json")).LoadAsync());
        Assert.Empty(
            new PFlowDispatchQueueStore(nine.FilePath("facebook_dispatch_queue.db")).Read()
        );
        Assert.Single(
            await new PFlowAccountStore(fourteen.FilePath("pflow_accounts.json")).LoadAsync()
        );
        Assert.Single(
            new PFlowDispatchQueueStore(fourteen.FilePath("facebook_dispatch_queue.db")).Read()
        );
    }

    [Fact]
    public async Task InvalidLegacyDataDoesNotPublishPartialProfileAndCanBeRetried()
    {
        Directory.CreateDirectory(Legacy);
        var source = Path.Combine(Legacy, "pflow_accounts.json");
        await File.WriteAllTextAsync(source, "broken");
        var profile = Profile("LDPlayer14");
        await Assert.ThrowsAnyAsync<JsonException>(() =>
            profile.InitializeAsync(new HashSet<string>(), false, Legacy)
        );
        Assert.False(Directory.Exists(profile.DirectoryPath));
        Assert.Equal("broken", await File.ReadAllTextAsync(source));
        File.Delete(source);
        await profile.InitializeAsync(new HashSet<string>(), false, Legacy);
        Assert.True(Directory.Exists(profile.DirectoryPath));
    }

    [Fact]
    public async Task AccountRemovalRevisionAndTombstonesStayWithTheirInstallation()
    {
        Directory.CreateDirectory(Legacy);
        await File.WriteAllTextAsync(
            Path.Combine(Legacy, "pflow_accounts.json"),
            JsonSerializer.Serialize(
                new
                {
                    Version = 1,
                    Accounts = Array.Empty<object>(),
                    RemovalRevision = 7,
                    RemovedBindings = new[] { Binding("nine"), Binding("fourteen") },
                }
            )
        );
        var profile = Profile("LDPlayer14");
        await profile.InitializeAsync(new HashSet<string> { "fourteen" }, false, Legacy);
        var state = await new PFlowAccountStore(
            profile.FilePath("pflow_accounts.json")
        ).ReadRemovalStateAsync();
        Assert.Equal(7, state.Revision);
        Assert.Equal("fourteen", Assert.Single(state.Bindings).InstanceIdentity);
    }

    [Fact]
    public async Task RecoverableLegacyBackupIsImportedWithoutChangingDamagedPrimary()
    {
        await SeedAsync();
        var source = Path.Combine(Legacy, "pflow_accounts.json");
        File.Copy(source, source + ".bak", true);
        await File.WriteAllTextAsync(source, "damaged");
        var profile = Profile("LDPlayer14");
        await profile.InitializeAsync(new HashSet<string> { "fourteen" }, false, Legacy);
        var account = Assert.Single(
            await new PFlowAccountStore(profile.FilePath("pflow_accounts.json")).LoadAsync()
        );
        Assert.Equal("fourteen", account.Binding.InstanceIdentity);
        Assert.Equal("damaged", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task ProtectedFilesAreCopiedOnlyForAccountsInSelectedInstallation()
    {
        await SeedAsync();
        Directory.CreateDirectory(Path.Combine(Legacy, "credentials"));
        Directory.CreateDirectory(Path.Combine(Legacy, "mailboxes"));
        var nineReference = Guid.NewGuid().ToString("N");
        var fourteenReference = Guid.NewGuid().ToString("N");
        var store = new PFlowAccountStore(Path.Combine(Legacy, "pflow_accounts.json"));
        await store.UpdateAsync(rows =>
        {
            rows.Single(a => a.Binding.InstanceIdentity == "nine").CredentialReference =
                nineReference;
            rows.Single(a => a.Binding.InstanceIdentity == "fourteen").CredentialReference =
                fourteenReference;
        });
        foreach (var reference in new[] { nineReference, fourteenReference })
            await File.WriteAllBytesAsync(
                Path.Combine(Legacy, "credentials", reference + ".bin"),
                [1, 2, 3]
            );
        foreach (var identity in new[] { "nine", "fourteen" })
            await File.WriteAllBytesAsync(
                Path.Combine(Legacy, "mailboxes", Binding(identity).AccountKey + ".bin"),
                [4, 5, 6]
            );
        var profile = Profile("LDPlayer14");
        await profile.InitializeAsync(new HashSet<string> { "fourteen" }, false, Legacy);
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            await File.ReadAllBytesAsync(
                profile.FilePath("credentials/" + fourteenReference + ".bin")
            )
        );
        Assert.False(File.Exists(profile.FilePath("credentials/" + nineReference + ".bin")));
        Assert.True(
            File.Exists(profile.FilePath("mailboxes/" + Binding("fourteen").AccountKey + ".bin"))
        );
        Assert.False(
            File.Exists(profile.FilePath("mailboxes/" + Binding("nine").AccountKey + ".bin"))
        );
        Assert.True(File.Exists(Path.Combine(Legacy, "credentials", nineReference + ".bin")));
    }
}
