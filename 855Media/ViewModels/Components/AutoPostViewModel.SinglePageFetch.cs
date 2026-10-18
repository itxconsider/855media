using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [RelayCommand]
    public async Task FetchSinglePageAsync(FacebookPageTargetViewModel? page)
    {
        if (
            page == null
            || page.IsProfile
            || page.AppClone != "FB-01"
            || IsBusy
            || IsRunning
            || !Pages.Contains(page)
        )
            return;
        var worker = Instances.SingleOrDefault(i => i.Index == ResolveDeviceIndex(page.DeviceName));
        if (worker == null)
        {
            _snackbarManager.Notify("Refresh devices before fetching this Page.");
            return;
        }
        IsBusy = true;
        SelectedManageInstance = worker;
        StatusSummary = $"Fetching only {page.PageName} on {worker.Name}...";
        _fetchCts?.Dispose();
        _fetchCts = new CancellationTokenSource();
        var ct = _fetchCts.Token;
        try
        {
            var accounts = await _accountStore.LoadAsync(ct);
            var account =
                accounts.SingleOrDefault(a =>
                    a.Binding.DeviceIndex == worker.Index
                    && a.Binding.OwnerUid == page.UserUid
                    && a.Binding.AppPackage == "com.facebook.katana"
                )
                ?? throw new InvalidOperationException(
                    "This Page needs a valid personal-account mapping before fetching."
                );
            await RequireAccountTransitionIdleAsync(worker.Index, ct);
            using var lease = await PFlowDeviceLease.AcquireAsync(worker.Index, ct);
            async Task Progress(string step) =>
                await ReportFetchProgressAsync(worker, "Fetch " + page.PageName, step);
            var navigator = new PFlowNativeAccountNavigator(
                _ldPlayerService,
                LdFolder,
                b => accounts.Single(a => a.Binding == b).Name,
                delay: PFlowFetchPacing.DelayAsync,
                requireRecoveryIdle: (b, c) => RequireAccountTransitionIdleAsync(b.DeviceIndex, c)
            );
            await PrepareFetchStartupAsync(worker, ct);
            await new PFlowSessionPreparation(_ldPlayerService, LdFolder)
            {
                AppOpenDelay = TimeSpan.FromSeconds(Math.Max(0, DelayOpenApp)),
            }.PrepareAsync(account.Binding, Progress, ct);
            await new PFlowAutomaticSessionService(
                navigator,
                new PFlowCredentialStore(),
                c => RequireAccountTransitionIdleAsync(worker.Index, c)
            ).EnsureAsync(account, Progress, ct, deviceLeaseAlreadyHeld: true);
            // Reuse the normal verified fetch and merge pipeline with exactly one Page.
            var details = new FacebookAccountDetails
            {
                Uid = account.Binding.OwnerUid,
                Name = account.Name,
                DeviceName = worker.Name,
                Pages =
                [
                    new FacebookPageInfo
                    {
                        PageId = page.PageId,
                        PersonaUid = page.PersonaUid,
                        PageName = page.PageName,
                        UserUid = page.UserUid,
                    },
                ],
            };
            await RunPageUiLookupsAsync(worker, details, ct);
            await PersistPagesSnapshotAsync();
        }
        catch (OperationCanceledException)
        {
            worker.Status = "Page fetch cancelled; Facebook left open.";
        }
        catch (Exception ex)
        {
            worker.Status = "Page fetch stopped: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
