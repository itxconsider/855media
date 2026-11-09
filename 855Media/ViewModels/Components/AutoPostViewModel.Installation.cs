using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly bool _usesInstallationData;
    private readonly string _installationDataRoot;
    private PFlowInstallationData? _installationData;
    private string? InstallationConfigPath =>
        _installationData?.FilePath("autopost_config.json")
        ?? (
            _usesInstallationData
                ? Path.Combine(_installationDataRoot, "autopost_config.json")
                : null
        );
    private string ActiveInstallationPath =>
        Path.Combine(_installationDataRoot, "active_ld_installation.txt");

    private void RequireInstallationData()
    {
        if (_usesInstallationData && _installationData == null)
            throw new InvalidOperationException(
                "Refresh devices to load this LDPlayer installation first."
            );
    }

    private PFlowCredentialStore CreateCredentialStore() =>
        new(_installationData?.FilePath("credentials"));

    private PFlowMailboxStore CreateMailboxStore() => new(_installationData?.FilePath("mailboxes"));

    private ScreenTimeHistoryStore CreateScreenTimeHistoryStore() =>
        new(_installationData?.FilePath("screentime_history.db"));

    private async Task SwitchInstallationDataAsync(IEnumerable<LDInstanceInfo> instances)
    {
        if (!_usesInstallationData)
            return;
        var next = new PFlowInstallationData(LdFolder, _installationDataRoot);
        if (next.Installation == _installationData?.Installation)
            return;
        var legacyConfig = await AutoPostConfigStorage.LoadConfigAsync(
            Path.Combine(_installationDataRoot, "autopost_config.json")
        );
        var original =
            !string.IsNullOrWhiteSpace(legacyConfig.LdFolder)
            && PFlowInstallationRouting.Normalize(legacyConfig.LdFolder) == next.Installation;
        await next.InitializeAsync(
            PFlowAccountInstallationScope.ReadIdentities(LdFolder, instances),
            original,
            _installationDataRoot
        );
        // Read everything before changing the active stores. A damaged destination
        // leaves the current installation and its live table intact.
        var pages = await PFlowPageStorage.LoadPagesAsync(next.FilePath("facebook_pages.json"));
        var credentials = new PFlowCredentialStore(next.FilePath("credentials"));
        var accounts = new PFlowAccountStore(next.FilePath("pflow_accounts.json"), credentials);
        await accounts.LoadAsync();
        var queue = new PFlowDispatchQueueStore(next.FilePath("facebook_dispatch_queue.db"));
        queue.Read();
        var config = await AutoPostConfigStorage.LoadConfigAsync(
            next.FilePath("autopost_config.json")
        );
        if (_installationData != null)
        {
            await PersistPagesSnapshotAsync();
            var oldConfig = ExportConfig();
            oldConfig.LdFolder = _installationData.Installation;
            await AutoPostConfigStorage.SaveConfigAsync(oldConfig, InstallationConfigPath);
        }
        DisposePostQueuePersistence();
        _queueStorageReady = false;
        Tasks.Clear();
        _preparedQueueWrites.Clear();
        _pFlowJobs.Clear();
        _pFlowTasks.Clear();
        _pFlowOwners.Clear();
        _pFlowActiveBindings.Clear();
        _mappedAccounts.Clear();
        CloseUpdatePage();
        CancelCredentialImport();
        CancelAccountRemap();
        CloseAddAccount();
        _managedAccountBinding = null;
        IsAccountManagementOpen = false;
        _configBeforeEdit = null;
        IsConfigOpen = false;
        SelectedManageInstance = null;
        _installationData = next;
        _pagesStoragePath = next.FilePath("facebook_pages.json");
        _accountStore = accounts;
        _pFlowHistory = new PFlowHistoryStore(next.FilePath("facebook_post_history.db"));
        _dispatchQueueStore = queue;
        _screenTimeService = new ScreenTimeService(
            _ldPlayerService,
            CreateScreenTimeHistoryStore()
        );
        config.LdFolder = LdFolder;
        ApplyConfig(config);
        Pages.Clear();
        foreach (var page in pages)
            Pages.Add(new FacebookPageTargetViewModel(page, Pages.Count + 1));
        // InitializePostQueue owns subscriptions and restart outcome handling.
        InitializePostQueue();
        await ReconcileSavedPostQueueAsync();
        await File.WriteAllTextAsync(ActiveInstallationPath, LdFolder);
        RefreshDisplayedManagePages();
        RefreshPFlowDashboard();
    }

    private async Task ResolveLdInstallationAsync()
    {
        var changed = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var actual = PFlowDeviceRouting.ResolveInstallationDirectory(
                _ldPlayerService,
                LdFolder
            );
            if (string.Equals(actual, LdFolder, StringComparison.Ordinal))
                return false;
            LdFolder = actual;
            return true;
        });
        if (!changed)
            return;
        await PFlowFetchTrace.WriteAsync(
            -1,
            "installation.path.resolved",
            "Account mapping and LD commands now use " + LdFolder
        );
        try
        {
            var config = await Dispatcher.UIThread.InvokeAsync(ExportConfig);
            if (!_usesInstallationData)
                await AutoPostConfigStorage.SaveConfigAsync(config, InstallationConfigPath);
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(-1, "installation.path.save-warning", ex.Message);
        }
    }
}
