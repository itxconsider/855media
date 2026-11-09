using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchedAccountDefaultsTests : IDisposable
{
    readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "855-fetch-defaults-" + Guid.NewGuid().ToString("N")
    );
    readonly PFlowAccountBinding binding = new(
        "fixture",
        45,
        "Alucard",
        "com.facebook.katana",
        "123"
    );
    PFlowAccountStore Store => new(Path.Combine(folder, "accounts.json"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    [Fact]
    public async Task NewAccountEnablesAllThreeOptionsAndRepeatedFetchPreservesEdits()
    {
        var store = Store;
        await store.RegisterFetchedAccountAsync(binding, "Owner", 0);
        var account = Assert.Single(await store.LoadAsync());
        Assert.True(
            account.SelectedForDiscovery
                && account.SelectedForScreenTime
                && account.SelectedForPosting
        );
        Assert.False(account.AllowAutomaticLogin || account.AllowAccountRotation);
        await store.UpdateAsync(accounts => accounts[0].SelectedForPosting = false);
        await store.RegisterFetchedAccountAsync(binding, "Owner", 0);
        Assert.False(Assert.Single(await store.LoadAsync()).SelectedForPosting);
    }

    [Fact]
    public async Task ExistingDisabledOptionsAreNotEnabled()
    {
        var store = Store;
        await store.UpdateAsync(accounts => accounts.Add(new() { Binding = binding }));
        await store.RegisterFetchedAccountAsync(binding, "Owner", 0);
        var account = Assert.Single(await store.LoadAsync());
        Assert.False(
            account.SelectedForDiscovery
                || account.SelectedForScreenTime
                || account.SelectedForPosting
        );
    }

    [Fact]
    public async Task LateRemovalCannotBeUndoneByDefaultRegistration()
    {
        var store = Store;
        await store.RegisterFetchedAccountAsync(binding, "Owner", 0);
        await store.RemoveAsync(binding);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RegisterFetchedAccountAsync(binding, "Owner", 0)
        );
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task CancelledFetchDoesNotCreateAccount()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Store.RegisterFetchedAccountAsync(binding, "Owner", 0, cancellation.Token)
        );
        Assert.Empty(await Store.LoadAsync());
    }
}
