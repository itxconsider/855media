using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [RelayCommand]
    public async Task FetchSinglePageAsync(FacebookPageTargetViewModel? page)
    {
        if (!CanUseSubscription())
            return;
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
        IsFetching = true;
        var ct = _fetchCts.Token;
        try
        {
            var requested = page.Model.CreateSnapshot();
            var removalRevision = (await _accountStore.ReadRemovalStateAsync(ct)).Revision;
            var accounts = await _accountStore.LoadAsync(ct);
            var account = PFlowSinglePageFetchRecovery.OwnerCandidate(
                requested,
                accounts,
                worker.Index
            );
            await RequireNativeFetchIdleAsync(worker.Index, ct);
            using var lease = await PFlowDeviceLease.AcquireAsync(worker.Index, ct, LdFolder);
            await RequireNativeFetchIdleAsync(worker.Index, ct);
            PFlowAccountStore.RequireCurrentBinding(
                account.Binding,
                (await _ldPlayerService.GetInstancesAsync(LdFolder, ct)).ToArray(),
                LdFolder
            );
            var useGraph = GetPagesByApi;
            using var policy = new PFlowNativeFetchScope(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                allowGraphSessionRead: useGraph
            );
            await using var keyboard = new PFlowKeyboardSession(
                _ldPlayerService,
                worker.Index,
                LdFolder
            );
            async Task Progress(string step) =>
                await ReportFetchProgressAsync(worker, "Fetch " + page.PageName, step);
            var mappingSnapshot = await Dispatcher.UIThread.InvokeAsync(() =>
                Pages.Select(p => p.Model.CreateSnapshot()).ToArray()
            );
            var navigator = new PFlowNativeAccountNavigator(
                _ldPlayerService,
                LdFolder,
                b => accounts.Single(a => a.Binding == b).Name,
                delay: PFlowFetchPacing.DelayAsync,
                requireRecoveryIdle: (b, c) => RequireNativeFetchIdleAsync(b.DeviceIndex, c),
                postingNameOnly: true,
                savedTargets: _ => mappingSnapshot
            );
            await PrepareFetchStartupAsync(worker, ct);
            await keyboard.CaptureAsync(ct);
            await new PFlowSessionPreparation(_ldPlayerService, LdFolder)
            {
                AppOpenDelay = TimeSpan.FromSeconds(Math.Max(0, DelayOpenApp)),
            }.PrepareAsync(account.Binding, Progress, ct);
            await new PFlowAutomaticSessionService(
                navigator,
                CreateCredentialStore(),
                c => RequireNativeFetchIdleAsync(worker.Index, c),
                postingNameOnly: true
            ).EnsureAsync(account, Progress, ct, deviceLeaseAlreadyHeld: true);
            var saved = await Dispatcher.UIThread.InvokeAsync(() =>
                Pages
                    .Where(p =>
                        ResolveDeviceIndex(p.DeviceName) == worker.Index
                        && p.AppClone == page.AppClone
                        && (p.UserUid == account.Binding.OwnerUid || ReferenceEquals(p, page))
                    )
                    .Select(p => p.Model.CreateSnapshot())
                    .ToArray()
            );
            // Invalidate only this requested Page; sibling configuration and selections
            // are not part of a single-Page refresh.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                page.Model.LivePageAccessVerified = false;
                page.Model.PageAccessIssue = PFlowPageAccessReconciliation.ResultIssue(
                    false,
                    page.Model.PageAccessIssue,
                    null
                );
                page.IsSelected = false;
                page.RefreshAccessStatus();
            });
            await PersistPagesSnapshotAsync();
            var fetch = new PFlowNativeFetch(
                _nativeFetchDeviceFactory?.Invoke(worker.Index, LdFolder, Progress)
                    ?? new PFlowNativeFetchDevice(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        Progress,
                        useGraph: useGraph
                    )
            );
            var result = await fetch.RunAsync(
                saved,
                Progress,
                ct,
                account.Name,
                account.Binding.OwnerUid,
                onlyPageName: page.PageName,
                readPersonalDetails: false
            );
            var fresh = result.Details?.Pages.SingleOrDefault(p =>
                PFlowSinglePageFetchRecovery.FreshPageMatches(requested, p, account.Binding)
            );
            if (fresh?.LivePageAccessVerified != true)
                throw new InvalidOperationException(
                    "The requested Page could not be verified; saved sources and data were retained."
                );
            ct.ThrowIfCancellationRequested();
            var removals = await _accountStore.ReadRemovalStateAsync(ct);
            if (
                removals.Revision != removalRevision
                || removals.Bindings.Contains(account.Binding)
                || !(await _accountStore.LoadAsync(ct)).Any(a => a.Binding == account.Binding)
            )
                throw new InvalidOperationException(
                    "Account choices changed during Fetch; Page data was retained."
                );
            PFlowAccountStore.RequireCurrentBinding(
                account.Binding,
                (await _ldPlayerService.GetInstancesAsync(LdFolder, ct)).ToArray(),
                LdFolder
            );
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (
                    !Pages.Contains(page)
                    || !PFlowSinglePageFetchRecovery.Unchanged(requested, page.Model)
                    || Pages.Any(p =>
                        !ReferenceEquals(p, page)
                        && p.Model.MatchesIdentity(
                            worker.Name,
                            page.AppClone,
                            fresh.UserUid,
                            fresh.PersonaUid,
                            fresh.PageId,
                            false,
                            "LD-" + worker.Index
                        )
                    )
                )
                    throw new InvalidOperationException(
                        "The Page row changed or duplicates another row; fetched data was not applied."
                    );
                page.UserUid = fresh.UserUid;
                page.PageId = fresh.PageId;
                page.PersonaUid = fresh.PersonaUid;
                page.Model.UserUid = fresh.UserUid;
                page.Model.PageId = fresh.PageId;
                page.Model.PersonaUid = fresh.PersonaUid;
                page.Model.AccountBinding = account.Binding;
                page.Model.ManagedBy = account.Name;
                page.Model.FetchDetailsPending = false;
            });
            await UpdatePageFromUiAsync(worker, fresh);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                page.Model.LivePageAccessVerified = true;
                page.Model.PageAccessIssue = fresh.PageAccessIssue;
                page.RefreshAccessStatus();
            });
            await PersistPagesSnapshotAsync();
            worker.Status = $"Page fetch complete: {page.PageName}";
            StatusSummary = worker.Status;
        }
        catch (OperationCanceledException)
        {
            await PFlowFetchTrace.WriteAsync(
                worker.Index,
                "native.fetch.single.cancelled",
                "Facebook left open."
            );
            worker.Status = "Page fetch cancelled; Facebook left open.";
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(
                worker.Index,
                "native.fetch.single.failed",
                ex.GetType().Name + ": " + ex.Message
            );
            worker.Status = "Page fetch stopped: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
