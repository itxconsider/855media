using System.Text;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowAddAccountTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "pflow-add-" + Guid.NewGuid().ToString("N")
    );
    private PFlowAccountStore Store => new(Path.Combine(directory, "accounts.json"));

    private static PFlowAddAccountInput Input() =>
        PFlowAddAccountInput.Parse(
            "123|fixture-password||10/3/2026|fixture@example.com",
            900074,
            "fixture-LD",
            "fixture",
            "com.facebook.katana"
        );

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Fact]
    public void ParsesMetadataWithoutExposingSecrets()
    {
        using var input = Input();
        Assert.Equal(new DateOnly(2026, 3, 10), input.AccountDate);
        Assert.Equal("123", Encoding.UTF8.GetString(input.Credential.LoginIdentifier));
        Assert.Equal("123", input.Binding.OwnerUid);
        Assert.DoesNotContain("fixture-password", input.ToString());
        input.Dispose();
        Assert.All(input.Credential.Password, b => Assert.Equal((byte)0, b));
    }

    [Theory]
    [InlineData("123|fixture-password")]
    [InlineData("123|fixture-password|")]
    [InlineData("123|fixture-password||fixture@example.com")]
    public void NewFormatUsesUidWithoutDate(string line)
    {
        using var input = PFlowAddAccountInput.Parse(
            line,
            900074,
            "fixture-LD",
            "fixture",
            "com.facebook.katana"
        );
        Assert.Equal("123", Encoding.UTF8.GetString(input.Credential.LoginIdentifier));
        Assert.Equal(default(DateOnly), input.AccountDate);
    }

    [Theory]
    [InlineData("123|fixture||31/2/2026|fixture@example.com")]
    [InlineData("123|fixture||2026-03-10|fixture@example.com")]
    [InlineData("123|fixture||10/3/2026|invalid")]
    [InlineData("123|fixture|bad!|10/3/2026|fixture@example.com")]
    [InlineData("123|fixture||10/3/2026|fixture@example.com|extra")]
    public void RejectsInvalidInput(string line) =>
        Assert.ThrowsAny<Exception>(() =>
            PFlowAddAccountInput.Parse(line, 1, "fixture-LD", "fixture", "com.facebook.katana")
        );

    [Fact]
    public async Task SavesOnlyAfterFreshPersonalProofAndLocksBinding()
    {
        using var input = Input();
        var credentials = new Credentials();
        var navigator = new Navigator(input.Binding);
        var saved = await new PFlowAddAccountService(Store, credentials, navigator).AddAsync(
            input,
            _ => Task.CompletedTask,
            default
        );
        Assert.True(saved.InstanceLocked);
        Assert.Equal("2026-03-10", saved.AccountDate);
        Assert.False(
            saved.SelectedForDiscovery || saved.SelectedForScreenTime || saved.SelectedForPosting
        );
        Assert.Equal(1, credentials.Saves);
        var json = await File.ReadAllTextAsync(Store.FilePath);
        Assert.DoesNotContain("fixture-password", json);
        Assert.Contains("opaque-fixture", json);
    }

    [Theory]
    [InlineData("wrong-uid")]
    [InlineData("cached")]
    [InlineData("page")]
    [InlineData("checkpoint")]
    [InlineData("stale")]
    public async Task RejectsUnverifiedIdentityWithoutSaving(string reason)
    {
        using var input = Input();
        var credentials = new Credentials();
        var nav = new Navigator(input.Binding) { Reason = reason };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowAddAccountService(Store, credentials, nav).AddAsync(
                input,
                _ => Task.CompletedTask,
                default
            )
        );
        Assert.Equal(0, credentials.Saves);
        Assert.Empty(await Store.LoadAsync());
    }

    [Fact]
    public async Task ExistingForeignBindingBlocksBeforeLogin()
    {
        using var input = Input();
        await Store.UpdateAsync(saved =>
            saved.Add(
                new()
                {
                    Binding = input.Binding with { DeviceIndex = 900073, DeviceName = "other" },
                }
            )
        );
        var navigator = new Navigator(input.Binding);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowAddAccountService(Store, new Credentials(), navigator).AddAsync(
                input,
                _ => Task.CompletedTask,
                default
            )
        );
        Assert.Equal(0, navigator.LoginCalls);
    }

    [Fact]
    public async Task LockedUidCannotBeAddedByLegacyMigrationToAnotherLd()
    {
        using var input = Input();
        await Store.UpdateAsync(saved =>
            saved.Add(new() { Binding = input.Binding, InstanceLocked = true })
        );
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            Store.UpdateAsync(saved =>
                saved.Add(
                    new()
                    {
                        Binding = input.Binding with { DeviceIndex = 900073, DeviceName = "other" },
                    }
                )
            )
        );
        Assert.Single(await Store.LoadAsync());
    }

    [Fact]
    public async Task CancellationBeforeLoginStoresNothing()
    {
        using var input = Input();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var credentials = new Credentials();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowAddAccountService(Store, credentials, new Navigator(input.Binding)).AddAsync(
                input,
                _ => Task.CompletedTask,
                cancel.Token
            )
        );
        Assert.Equal(0, credentials.Saves);
    }

    [Fact]
    public void InstalledLoginFixtureRequiresUniqueEnabledControls()
    {
        string Node(string role, string kind, string password) =>
            $"<node package='com.facebook.katana' enabled='true' class='{kind}' password='{password}' clickable='true' content-desc='{role}' bounds='[64,530][584,570]'/>";
        var login = Node("Mobile number or email,", "android.widget.EditText", "false");
        var password = Node("Password,", "android.widget.EditText", "true");
        Assert.Equal(
            (324, 550),
            PFlowNativeLoginNavigator.Control(
                "<hierarchy>" + login + password + "</hierarchy>",
                "login"
            )
        );
        Assert.Null(
            PFlowNativeLoginNavigator.Control(
                "<hierarchy>" + login + login + "</hierarchy>",
                "login"
            )
        );
        Assert.Null(
            PFlowNativeLoginNavigator.Control(
                "<hierarchy>" + login.Replace("enabled='true'", "enabled='false'") + "</hierarchy>",
                "login"
            )
        );
        Assert.Null(PFlowNativeLoginNavigator.Control("invalid", "password"));
    }

    private sealed class Navigator(PFlowAccountBinding binding) : IPFlowLoginNavigator
    {
        public string Reason { get; init; } = "";
        public int LoginCalls { get; private set; }

        public Task<PFlowWorkerIdentity> ReadWorkerAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new PFlowWorkerIdentity(
                    binding.DeviceIndex,
                    binding.DeviceName,
                    binding.InstanceIdentity,
                    binding.AppPackage
                )
            );

        public Task<PFlowAccountObservation> LoginAsync(
            PFlowAccountBinding b,
            PFlowCredential c,
            Func<string, Task> progress,
            CancellationToken ct
        )
        {
            LoginCalls++;
            return Task.FromResult(
                new PFlowAccountObservation(
                    Reason == "wrong-uid" ? "999" : binding.OwnerUid,
                    binding.AppPackage,
                    Reason != "page",
                    true,
                    Reason != "cached",
                    DateTimeOffset.UtcNow.AddSeconds(Reason == "stale" ? -30 : 0),
                    Reason == "checkpoint"
                        ? PFlowAuthenticationState.Checkpoint
                        : PFlowAuthenticationState.Authenticated
                )
            );
        }
    }

    private sealed class Credentials : IPFlowCredentialStore
    {
        public int Saves { get; private set; }

        public Task<string> SaveAsync(
            PFlowAccountBinding b,
            PFlowCredential c,
            CancellationToken ct = default
        )
        {
            Saves++;
            return Task.FromResult("opaque-fixture");
        }

        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding b,
            string reference,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public void Delete(string reference) { }
    }
}
