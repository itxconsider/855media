using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowFetchedAccountRestoreTests : IDisposable
{
    readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "pflow-fetch-restore-" + Guid.NewGuid().ToString("N")
    );
    readonly PFlowAccountBinding binding = new(
        "instance-A",
        20,
        "LD-20",
        "com.facebook.katana",
        "61594750165531"
    );
    PFlowAccountStore Store => new(Path.Combine(folder, "accounts.json"));

    async Task<PFlowAccountRemovalState> Removed()
    {
        Directory.CreateDirectory(folder);
        await Store.UpdateAsync(a =>
            a.Add(
                new()
                {
                    Binding = binding,
                    Name = "William",
                    CredentialReference = "old",
                    SelectedForPosting = true,
                }
            )
        );
        await Store.RemoveAsync(binding);
        return await Store.ReadRemovalStateAsync();
    }

    [Fact]
    public async Task ExplicitFetchRestoresExactBindingWithoutOldCredentialsOrSelections()
    {
        var state = await Removed();
        var other = binding with
        {
            DeviceIndex = 21,
            DeviceName = "LD-21",
            InstanceIdentity = "other",
            OwnerUid = "123456789",
        };
        await Store.UpdateAsync(a => a.Add(new() { Binding = other }));
        await Store.RemoveAsync(other);
        state = await Store.ReadRemovalStateAsync();
        await Store.RestoreFetchedBindingAsync(binding, "William Rhodes", state.Revision);
        var restored = Assert.Single(await Store.LoadAsync());
        Assert.Equal(binding, restored.Binding);
        Assert.Equal("William Rhodes", restored.Name);
        Assert.False(
            restored.SelectedForDiscovery
                || restored.SelectedForPosting
                || restored.SelectedForScreenTime
                || restored.AllowAutomaticLogin
        );
        Assert.Empty(restored.CredentialReference);
        Assert.Empty(restored.Pages);
        Assert.Equal(new[] { other }, await Store.RemovedBindingsAsync());
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("index")]
    [InlineData("name")]
    [InlineData("identity")]
    [InlineData("app")]
    public async Task DifferentBindingCannotClearOldRemoval(string change)
    {
        var state = await Removed();
        var next = change switch
        {
            "owner" => binding with { OwnerUid = "987654321" },
            "index" => binding with { DeviceIndex = 21 },
            "name" => binding with { DeviceName = "Renamed LD" },
            "identity" => binding with { InstanceIdentity = "replacement-instance" },
            _ => binding with { AppPackage = "other.app" },
        };
        var before = await File.ReadAllBytesAsync(Store.FilePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store.RestoreFetchedBindingAsync(next, "William", state.Revision)
        );
        Assert.Equal(before, await File.ReadAllBytesAsync(Store.FilePath));
    }

    [Fact]
    public async Task LaterRemovalWinsOverAnInFlightFetch()
    {
        var state = await Removed();
        var other = binding with { OwnerUid = "123456789" };
        await Store.UpdateAsync(a => a.Add(new() { Binding = other }));
        await Store.RemoveAsync(other);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store.RestoreFetchedBindingAsync(binding, "William", state.Revision)
        );
        Assert.Contains(binding, await Store.RemovedBindingsAsync());
        Assert.Empty(await Store.LoadAsync());
    }

    [Fact]
    public async Task FetchCannotUndoAnActiveTransfer()
    {
        await Removed();
        await Store.UpdateAsync(a =>
            a.Add(
                new()
                {
                    Binding = binding with
                    {
                        DeviceIndex = 21,
                        DeviceName = "LD-21",
                        InstanceIdentity = "destination",
                    },
                    TransferredFromBindings = [binding],
                }
            )
        );
        var state = await Store.ReadRemovalStateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store.RestoreFetchedBindingAsync(binding, "William", state.Revision)
        );
        Assert.Contains(binding, await Store.RemovedBindingsAsync());
    }

    [Fact]
    public async Task OlderDocumentWithoutRevisionCanBeExplicitlyRestored()
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Store.FilePath,
            JsonSerializer.Serialize(
                new
                {
                    Version = 1,
                    Accounts = Array.Empty<PFlowPersonalAccount>(),
                    RemovedBindings = new[] { binding },
                }
            )
        );
        var state = await Store.ReadRemovalStateAsync();
        Assert.Equal(0, state.Revision);
        await Store.RestoreFetchedBindingAsync(binding, "William", state.Revision);
        Assert.Empty(await Store.RemovedBindingsAsync());
        Assert.Single(await Store.LoadAsync());
    }

    [Fact]
    public async Task CancellationLeavesRemovalUnchanged()
    {
        var state = await Removed();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Store.RestoreFetchedBindingAsync(
                binding,
                "William",
                state.Revision,
                new CancellationToken(true)
            )
        );
        Assert.Contains(binding, await Store.RemovedBindingsAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }
}
