using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private async Task RequireNativeFetchIdleAsync(int index, CancellationToken ct)
    {
        if (
            PFlowJobStartRecovery.HasPendingPublication(
                (await _pFlowHistory.ReadAsync(ct)).Concat(_pFlowJobs),
                index
            )
        )
            throw new InvalidOperationException("A pending or uncertain publication blocks Fetch.");
        await RequireAccountTransitionIdleAsync(index, ct);
    }

    private async Task<bool> ProcessNativeFetchAsync(
        LDInstanceItemViewModel worker,
        bool autoCloseIfBooted,
        CancellationToken ct
    )
    {
        using var lease = await PFlowDeviceLease.AcquireAsync(worker.Index, ct, LdFolder);
        var useGraph = GetPagesByApi;
        var skipAccounts = SkipVerifiedFetchAccounts;
        var skipPages = SkipVerifiedFetchPages;
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
        var wasRunning = (await ReadAssignedFetchInstanceAsync(worker.Index, ct))
            .Single(i => i.Index == worker.Index)
            .IsRunning;
        var completed = false;
        var status = "";
        Task Progress(string step) =>
            ReportFetchProgressAsync(worker, "Fetching account", worker.Name + ": " + step);
        try
        {
            await RequireNativeFetchIdleAsync(worker.Index, ct);
            var removalState = await _accountStore.ReadRemovalStateAsync(ct);
            await PrepareFetchStartupAsync(worker, ct);
            await keyboard.CaptureAsync(ct);
            await FetchDelayAsync(worker, DelayOpenApp, "Before opening Facebook", ct);
            if (
                !await _ldPlayerService.WaitForFacebookReadyAsync(
                    worker.Index,
                    TimeSpan.FromSeconds(120),
                    LdFolder,
                    ct,
                    Progress
                )
            )
                throw new InvalidOperationException(
                    "Facebook or the internet connection was not ready for Fetch."
                );
            await PFlowSession.EnsureFetchFacebookForegroundAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                ct,
                Progress
            );
            var accounts = (await _accountStore.LoadAsync(ct))
                .Where(a =>
                    a.Binding.DeviceIndex == worker.Index
                    && a.Binding.AppPackage == "com.facebook.katana"
                )
                .ToArray();
            var selected = accounts
                .Where(a => a.SelectedForDiscovery)
                .OrderBy(a => a.RotationOrder)
                .ThenBy(a => a.Binding.OwnerUid, StringComparer.Ordinal)
                .ToArray();
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
            var automatic = new PFlowAutomaticSessionService(
                navigator,
                CreateCredentialStore(),
                c => RequireNativeFetchIdleAsync(worker.Index, c),
                postingNameOnly: true
            );
            var runs =
                selected.Length == 0
                    ? new PFlowPersonalAccount?[] { null }
                    : selected.Cast<PFlowPersonalAccount?>().ToArray();
            var verified = 0;
            var unresolved = 0;
            var skippedAccounts = 0;
            var skippedPages = 0;
            var processedAccounts = 0;
            foreach (var account in runs)
            {
                ct.ThrowIfCancellationRequested();
                await RequireNativeFetchIdleAsync(worker.Index, ct);
                if (account != null)
                {
                    PFlowAccountStore.RequireCurrentBinding(
                        account.Binding,
                        (await ReadAssignedFetchInstanceAsync(worker.Index, ct)).ToArray(),
                        LdFolder
                    );
                    await automatic.EnsureAsync(
                        account,
                        Progress,
                        ct,
                        deviceLeaseAlreadyHeld: true
                    );
                }
                else
                    await Progress(
                        "No Fetch accounts selected; scanning the current account without changing saved selections."
                    );
                var saved = await Dispatcher.UIThread.InvokeAsync(() =>
                    Pages
                        .Where(p =>
                            ResolveDeviceIndex(p.DeviceName) == worker.Index
                            && p.AppClone == worker.FbClone
                        )
                        .Select(p => p.Model.CreateSnapshot())
                        .ToArray()
                );
                var skipProfileDetails =
                    skipAccounts && account != null && PFlowFetchSkipPolicy.Account(account, saved);
                if (skipProfileDetails)
                {
                    skippedAccounts++;
                    await Progress(
                        "Keeping verified profile details; discovering Pages: " + account!.Name
                    );
                }
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
                    account?.Name,
                    account?.Binding.OwnerUid,
                    readPersonalDetails: !skipProfileDetails,
                    collected: update =>
                        ApplyFetchCollectedAsync(worker, update, removalState.Revision, ct),
                    skipVerifiedPages: skipPages
                );
                unresolved += result.Unresolved.Length;
                if (result.Details == null)
                {
                    await Progress(
                        $"{result.OwnerName}: numeric account ID is unresolved. No new identity was saved; existing configuration retained."
                    );
                    status = "Fetch incomplete: account could not be identified";
                    worker.Status = status;
                    return false;
                }
                result.Details.DeviceName = worker.Name;
                var restoredBinding = new PFlowAccountBinding(
                    PFlowDeviceRouting.ReadIdentity(LdFolder, worker.Index),
                    worker.Index,
                    worker.Name,
                    "com.facebook.katana",
                    result.Details.Uid
                );
                PFlowAccountStore.RequireCurrentBinding(
                    restoredBinding,
                    (await ReadAssignedFetchInstanceAsync(worker.Index, ct)).ToArray(),
                    LdFolder
                );
                if (
                    saved.Any(p =>
                        p.UserUid == result.Details.Uid
                        && p.AccountBinding != null
                        && p.AccountBinding != restoredBinding
                    )
                )
                    throw new InvalidOperationException(
                        "The fetched account's saved LD/app binding changed; review its mapping before restoring it."
                    );
                if (removalState.Bindings.Contains(restoredBinding))
                {
                    await RequireNativeFetchIdleAsync(worker.Index, ct);
                    PFlowAccountStore.RequireCurrentBinding(
                        restoredBinding,
                        (await ReadAssignedFetchInstanceAsync(worker.Index, ct)).ToArray(),
                        LdFolder
                    );
                    await _accountStore.RestoreFetchedBindingAsync(
                        restoredBinding,
                        result.Details.Name,
                        removalState.Revision,
                        ct
                    );
                    await Progress(
                        "Restored the explicitly fetched account's exact LD/app binding; other removal records are unchanged."
                    );
                }
                verified += result.Details.Pages.Count(p => p.LivePageAccessVerified == true);
                processedAccounts++;
                skippedPages += result.SkippedPageIds.Length;
                // Retain absent saved identities without treating a switcher miss as deletion.
                // New native results keep their access state; retained candidates remain unverified.
                result.Details.RetainSavedPageIdentities(
                    saved.Where(p => !result.SkippedPageIds.Contains(p.PageId)),
                    worker.Name,
                    worker.FbClone,
                    "LD-" + worker.Index
                );
                await ReplaceDevicePagesInTableAsync(
                    worker,
                    result.Details,
                    isRootSnapshot: false,
                    retainedPageIds: result.SkippedPageIds.ToHashSet()
                );
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (
                        var row in Pages.Where(p =>
                            p.UserUid == result.Details.Uid
                            && p.DeviceName == worker.Name
                            && p.AppClone == worker.FbClone
                        )
                    )
                        row.Model.AccountBinding = restoredBinding;
                });
                await PersistPagesSnapshotAsync();
                await _accountStore.RegisterFetchedAccountAsync(
                    restoredBinding,
                    result.Details.Name,
                    removalState.Revision,
                    ct
                );
                await RefreshFetchedAccountMappingsAsync(worker);
                if (result.Unresolved.Length > 0)
                    await Progress("Could not identify: " + string.Join(", ", result.Unresolved));
            }
            completed = true;
            status =
                $"Fetch complete: {processedAccounts} account(s), {verified} Pages checked, {unresolved} unresolved"
                + (
                    skippedAccounts + skippedPages > 0
                        ? $"; kept {skippedAccounts} verified profile(s), skipped {skippedPages} Page(s)"
                        : ""
                );
            worker.Status = status;
            return true;
        }
        catch (OperationCanceledException)
        {
            await PFlowFetchTrace.WriteAsync(
                worker.Index,
                "native.fetch.cancelled",
                "Facebook left open."
            );
            worker.Status = "Fetch cancelled; Facebook left open.";
            throw;
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(
                worker.Index,
                "native.fetch.failed",
                ex.GetType().Name + ": " + ex.Message
            );
            worker.Status = "Fetch stopped: " + ex.Message;
            return false;
        }
        finally
        {
            if (!await keyboard.RestoreAsync())
                worker.Status += " (keyboard restore pending)";
            if (completed)
            {
                using var close = new CancellationTokenSource(
                    TimeSpan.FromSeconds(Math.Max(0, DelayBeforeCloseLD) + 30)
                );
                await RequireAccountTransitionIdleAsync(worker.Index, close.Token);
                if (IsClearRecentApp)
                    await PFlowSession.ClearAppsAsync(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        close.Token
                    );
                if (autoCloseIfBooted && (!wasRunning || CloseLDAfterAction))
                {
                    await FetchDelayAsync(
                        worker,
                        DelayBeforeCloseLD,
                        "Before closing LD",
                        close.Token
                    );
                    await _ldPlayerService.QuitInstanceAsync(worker.Index, LdFolder, close.Token);
                    worker.Status = status + "; Closed";
                }
            }
        }
    }
}
