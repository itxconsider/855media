using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private async Task DiscoverUncachedPagesAsync(
        LDInstanceItemViewModel worker,
        FacebookAccountDetails details,
        CancellationToken ct
    )
    {
        await new PFlowPersonaSwitcherService(_ldPlayerService).WarmPageDiscoveryAsync(
            worker.Index,
            details.Name,
            details.Uid,
            LdFolder,
            step => ReportFetchProgressAsync(worker, "Page discovery", step),
            ct
        );
        var root = await _ldPlayerService.ExtractFacebookAccountsFromRootShellAsync(
            worker.Index,
            "com.facebook.katana",
            LdFolder,
            ct
        );
        var added = root.Where(a => a.Type == "Page" && a.UserUid == details.Uid)
            .Select(a => a.ToFacebookPageInfo());
        foreach (var page in added)
            if (!details.Pages.Any(p => p.PageId == page.PageId))
                details.Pages.Add(page);
        await ReplaceDevicePagesInTableAsync(worker, details);
        await PersistPagesSnapshotAsync();
        await RefreshFetchedAccountMappingsAsync(worker);
    }

    private async Task FetchDelayAsync(
        LDInstanceItemViewModel worker,
        int seconds,
        string reason,
        CancellationToken ct
    )
    {
        if (seconds <= 0)
            return;
        await ReportFetchProgressAsync(worker, reason, $"{worker.Name}: {reason} ({seconds}s).");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    private async Task PrepareFetchStartupAsync(
        LDInstanceItemViewModel worker,
        CancellationToken ct
    )
    {
        var instance = (await _ldPlayerService.GetInstancesAsync(LdFolder, ct)).Single(i =>
            i.Index == worker.Index
        );
        if (!instance.IsAndroidStarted)
        {
            await _bootGate.WaitAsync(ct);
            try
            {
                if (!await _ldPlayerService.LaunchInstanceAsync(worker.Index, LdFolder, ct))
                    throw new InvalidOperationException("Assigned LD did not start.");
                await FetchDelayAsync(worker, DelayOpenEachLD, "Between LD starts", ct);
            }
            finally
            {
                _bootGate.Release();
            }
            if (
                !await _ldPlayerService.WaitForBootCompletedAsync(
                    worker.Index,
                    PFlowStartupTimeouts.AndroidBoot,
                    LdFolder,
                    ct
                )
            )
                throw new InvalidOperationException("Android boot timed out after 180 seconds.");
            await FetchDelayAsync(worker, DelayAfterOpenLD, "After LD opens", ct);
        }
        await PrepareFetchFacebookAsync(worker, ct);
    }

    private async Task PrepareFetchFacebookAsync(
        LDInstanceItemViewModel worker,
        CancellationToken ct
    )
    {
        await ArrangeFetchWindowsAsync(ct);
        if (CheckSurfsharkConnection)
        {
            await ReportFetchProgressAsync(
                worker,
                "Waiting for Surfshark connection",
                "Checking Surfshark VPN inside LD (up to 180 seconds)."
            );
            await PFlowConnectionChecks.WaitForSurfsharkAsync(
                _ldPlayerService,
                worker.Index,
                LdFolder,
                ct
            );
        }

        await RequireAccountTransitionIdleAsync(worker.Index, ct);
        if (
            ClearCacheFBBeforeOpen
            && !await _ldPlayerService.ClearFacebookCacheAsync(worker.Index, LdFolder, ct)
        )
            throw new InvalidOperationException("Facebook cache could not be cleared.");
    }

    private async Task<bool> FetchMappedAccountsAsync(
        LDInstanceItemViewModel worker,
        PFlowPersonalAccount[] accounts,
        bool autoCloseIfBooted,
        CancellationToken ct
    )
    {
        var selected = accounts
            .Where(a => a.SelectedForDiscovery)
            .OrderBy(a => a.RotationOrder)
            .ThenBy(a => a.Binding.OwnerUid, StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
        {
            worker.Status = "Fetch skipped: no personal accounts selected for fetching.";
            return true;
        }
        using var lease = await PFlowDeviceLease.AcquireAsync(worker.Index, ct);
        var wasRunning = (await _ldPlayerService.GetInstancesAsync(LdFolder, ct))
            .Single(i => i.Index == worker.Index)
            .IsRunning;
        var navigator = new PFlowNativeAccountNavigator(
            _ldPlayerService,
            LdFolder,
            b => accounts.Single(a => a.Binding == b).Name,
            delay: PFlowFetchPacing.DelayAsync,
            requireRecoveryIdle: (binding, c) =>
                RequireAccountTransitionIdleAsync(binding.DeviceIndex, c)
        );
        var service = new PFlowAutomaticSessionService(
            navigator,
            new PFlowCredentialStore(),
            c => RequireAccountTransitionIdleAsync(worker.Index, c)
        );
        var completed = false;
        var verifiedPages = 0;
        var attemptedPages = 0;
        try
        {
            await PrepareFetchStartupAsync(worker, ct);
            foreach (var account in selected)
            {
                async Task Progress(string step) =>
                    await ReportFetchProgressAsync(
                        worker,
                        "Account " + account.Binding.OwnerUid,
                        worker.Name + ": " + step
                    );
                await new PFlowSessionPreparation(_ldPlayerService, LdFolder)
                {
                    AppOpenDelay = TimeSpan.FromSeconds(Math.Max(0, DelayOpenApp)),
                }.PrepareAsync(account.Binding, Progress, ct);
                await service.EnsureAsync(account, Progress, ct, deviceLeaseAlreadyHeld: true);
                var root = await _ldPlayerService.ExtractFacebookAccountsFromRootShellAsync(
                    worker.Index,
                    account.Binding.AppPackage,
                    LdFolder,
                    ct
                );
                var owner =
                    root.SingleOrDefault(a =>
                        a.Type == "Profile" && a.Uid == account.Binding.OwnerUid
                    )
                    ?? throw new InvalidOperationException(
                        "Verified personal account is missing from discovery; no other owner was substituted."
                    );
                var details = new FacebookAccountDetails
                {
                    DeviceName = worker.Name,
                    Uid = account.Binding.OwnerUid,
                    Name = string.IsNullOrWhiteSpace(owner.Name) ? account.Name : owner.Name,
                    Email = owner.Email,
                    ProfilePictureUrl = owner.PicUrl,
                    Pages = root.Where(a =>
                            a.Type == "Page" && a.UserUid == account.Binding.OwnerUid
                        )
                        .GroupBy(a => a.PageId)
                        .Select(g => g.First().ToFacebookPageInfo())
                        .ToList(),
                };
                await ReplaceDevicePagesInTableAsync(worker, details);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (
                        var row in Pages.Where(p =>
                            ResolveDeviceIndex(p.DeviceName) == worker.Index
                            && p.UserUid == account.Binding.OwnerUid
                            && p.AppClone == "FB-01"
                        )
                    )
                        row.Model.AccountBinding = account.Binding;
                });
                await PersistPagesSnapshotAsync();
                var personal = await _ldPlayerService.ExtractPersonalAccountDetailsFromUiAsync(
                    worker.Index,
                    details.Uid,
                    details.Name,
                    LdFolder,
                    ct,
                    Progress
                );
                if (personal?.SwitchedToProfile != true)
                    throw new InvalidOperationException(
                        "Selected personal profile could not be confirmed for discovery."
                    );
                details.SwitchedToProfile = true;
                details.Name = PreferValue(personal.Name, details.Name);
                details.Email = PreferValue(personal.Email, details.Email);
                details.Birthdate = personal.Birthdate;
                details.CreationDate = personal.CreationDate;
                if (!string.IsNullOrWhiteSpace(personal.ProfilePictureUrl))
                    details.ProfilePictureUrl = personal.ProfilePictureUrl;
                details.Location = personal.Location;
                details.Hometown = personal.Hometown;
                details.Work = personal.Work;
                details.Education = personal.Education;
                details.RelationshipStatus = personal.RelationshipStatus;
                await DiscoverUncachedPagesAsync(worker, details, ct);
                var pageResults = await RunPageUiLookupsAsync(worker, details, ct);
                verifiedPages += pageResults.Succeeded;
                attemptedPages += pageResults.Attempted;
                await ReplaceDevicePagesInTableAsync(worker, details, isRootSnapshot: false);
                await PersistPagesSnapshotAsync();
                // Root/cache enumeration cannot establish that a missing Page was removed.
                var runId = Guid.NewGuid().ToString("N");
                PFlowAccountReconciliation.Apply(
                    account,
                    new(
                        details.Uid,
                        runId,
                        false,
                        details
                            .Pages.Select(p => new PFlowLivePage(
                                p.PageId,
                                p.PersonaUid,
                                p.PageName,
                                p.LivePageAccessVerified == true
                            ))
                            .ToArray()
                    ),
                    DateTimeOffset.UtcNow
                );
                await _accountStore.UpdateAsync(
                    saved =>
                    {
                        var target = saved.Single(a => a.Binding == account.Binding);
                        target.Name = details.Name;
                        target.Email = details.Email;
                        target.LastVerifiedUtc = account.LastVerifiedUtc;
                        target.Authentication = account.Authentication;
                        target.Pages = account.Pages;
                    },
                    ct
                );
                await RequireAccountTransitionIdleAsync(worker.Index, ct);
                if (
                    !await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                        worker.Index,
                        details.Name,
                        LdFolder,
                        ct
                    )
                )
                    throw new InvalidOperationException(
                        "Personal identity could not be restored after Page discovery."
                    );
            }
            completed = true;
            worker.Status =
                $"Fetched {selected.Length} personal accounts · {verifiedPages}/{attemptedPages} Pages verified.";
            return true;
        }
        catch (OperationCanceledException)
        {
            worker.Status = "Account fetch cancelled; Facebook left open.";
            throw;
        }
        catch (Exception ex)
        {
            worker.Status = "Account fetch stopped: " + ex.Message;
            return false;
        }
        finally
        {
            // Keep authentication challenges and failed observations visible for user action.
            if (completed)
            {
                using var cleanup = new CancellationTokenSource(
                    TimeSpan.FromSeconds(Math.Max(0, DelayBeforeCloseLD) + 30)
                );
                await RequireAccountTransitionIdleAsync(worker.Index, cleanup.Token);
                if (IsClearRecentApp)
                    await PFlowSession.ClearAppsAsync(
                        _ldPlayerService,
                        worker.Index,
                        LdFolder,
                        cleanup.Token
                    );
                if (autoCloseIfBooted && (!wasRunning || CloseLDAfterAction))
                {
                    await FetchDelayAsync(
                        worker,
                        DelayBeforeCloseLD,
                        "Before closing LD",
                        cleanup.Token
                    );
                    await _ldPlayerService.QuitInstanceAsync(worker.Index, LdFolder, cleanup.Token);
                }
            }
        }
    }
}
