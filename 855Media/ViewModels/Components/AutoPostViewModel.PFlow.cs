using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private readonly PFlowHistoryStore _pFlowHistory = new();
    private readonly List<PFlowJob> _pFlowJobs = [];
    private readonly Dictionary<string, AutoPostTaskItem> _pFlowTasks = new();
    private bool _pFlowBatchDryRun;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> _pFlowOwners =
        new();

    [ObservableProperty]
    private bool _pFlowDryRun;

    private async Task PreparePFlowAsync(WorkerRunConfig config)
    {
        var selectedDeviceIndices = (ShowOnlyOpen ? DisplayedInstances : Instances)
            .Where(w => w.IsSelected)
            .Select(w => w.Index)
            .ToArray();
        foreach (
            var task in Tasks.Where(t =>
                selectedDeviceIndices.Contains(ResolveDeviceIndex(t.DeviceName))
            )
        )
            PFlowTaskRouting.RestoreUnmatchedQueueRow(task);
        if (
            !config.ScreenTimeOnly
            && !config.ScreenTime.HasActions
            && !Tasks.Any(t =>
                PFlowTaskRouting.IsQueuedPost(t)
                && selectedDeviceIndices.Contains(ResolveDeviceIndex(t.DeviceName))
            )
        )
            throw new InvalidOperationException(
                "No queued posts for the selected LDs. Prepare the post queue before starting PFlow again."
            );
        await PersistAccountSelectionDraftsAsync(selectedDeviceIndices);
        await LoadAccountMappingsAsync();
        _pFlowRunAccounts = System.Text.Json.JsonSerializer.Deserialize<PFlowPersonalAccount[]>(
            System.Text.Json.JsonSerializer.Serialize(_mappedAccounts)
        )!;
        _pFlowSessions.Clear();
        _pFlowActiveBindings.Clear();
        _pFlowAccountAttention.Clear();
        _pFlowJobs.Clear();
        _pFlowTasks.Clear();
        _pFlowOwners.Clear();
        _postTargetSessions.Clear();
        _nativeIdentityScopes.Clear();
        _graphActivationProofs.Clear();
        _postProcessIds.Clear();
        _pFlowBatchDryRun = PFlowDryRun;
        var uniqueAcrossTargets = UploadUniqueFile;
        var selectedWorkers = (ShowOnlyOpen ? DisplayedInstances : Instances)
            .Where(w => w.IsSelected)
            .ToList();
        if (
            config.ScreenTimeOnly
            && !_pFlowRunAccounts.Any(a =>
                a.SelectedForScreenTime
                && selectedDeviceIndices.Contains(a.Binding.DeviceIndex)
                && a.Binding.AppPackage == "com.facebook.katana"
            )
        )
            throw new InvalidOperationException(
                "Select ScreenTime for at least one mapped personal account on the selected LDs."
            );
        if (Tasks.Any(t => t.Status == "Queued" && ResolveDeviceIndex(t.DeviceName) < 0))
            throw new InvalidOperationException(
                "A queued target has no assigned LD instance. Review its mapping; no fallback was used."
            );
        var screenTimeHistory = new ScreenTimeHistoryStore();
        if (selectedWorkers.Any(w => screenTimeHistory.NeedsReview(w.Index)))
            throw new InvalidOperationException(
                "Review uncertain ScreenTime interactions in ScreenTime history before reusing that device."
            );
        await _pFlowHistory.RecoverAsync();
        var history = await _pFlowHistory.ReadAsync();
        if (config.ScreenTimeOnly)
            return;
        var batch = Guid.NewGuid().ToString("N");
        foreach (var worker in selectedWorkers)
        {
            var devicePages = Pages
                .Where(p => ResolveDeviceIndex(p.DeviceName) == worker.Index)
                .ToList();
            var accountPlan = PFlowAutomaticSessionService.Plan(
                _pFlowRunAccounts,
                worker.Index,
                "com.facebook.katana",
                config.ScreenTime.HasActions,
                true
            );
            foreach (var selectedAccount in accountPlan)
                PFlowAccountStore.RequireCurrentBinding(
                    selectedAccount.Binding,
                    Instances.Select(i => i.Model).ToArray(),
                    LdFolder
                );
            foreach (var page in devicePages)
            {
                if (!page.IsProfile && page.Model?.LivePageAccessVerified == false)
                {
                    foreach (
                        var task in Tasks.Where(t =>
                            t.Status == "Queued"
                            && ResolveDeviceIndex(t.DeviceName) == worker.Index
                            && t.App == page.AppClone
                            && t.PageId == page.PageId
                        )
                    )
                        task.Status = "Blocked: Page access not verified; fetch its account again.";
                    continue;
                }

                var account = _pFlowRunAccounts.SingleOrDefault(a =>
                    a.Binding.DeviceIndex == worker.Index
                    && a.Binding.OwnerUid == page.UserUid
                    && a.Binding.AppPackage == "com.facebook.katana"
                );
                if (account == null || !account.SelectedForPosting)
                    continue;
                var targetModel =
                    page.Model ?? throw new InvalidOperationException("Target model is missing.");
                foreach (var type in new[] { "Reel", "Picture", "Story" })
                {
                    if (
                        !(
                            type == "Story" ? config.PostStory
                            : type == "Reel" ? config.PostReel
                            : type == "Picture" ? config.PostPicture
                            : config.PostVideo
                        )
                    )
                        continue;
                    var queued = Tasks
                        .Where(t =>
                            t.Status == "Queued"
                            && ResolveDeviceIndex(t.DeviceName) == worker.Index
                            && PFlowTaskRouting.MatchesTarget(
                                t,
                                targetModel,
                                devicePages.Select(p => p.Model)
                            )
                            && (
                                t.Type == type
                                || type == "Picture" && t.Type == "Photo"
                                || type == "Reel" && t.Type == "Video"
                            )
                        )
                        .ToList();
                    if (queued.Count == 0)
                        continue;
                    PFlowAccountStore.RequireCurrentBinding(
                        account.Binding,
                        Instances.Select(i => i.Model).ToArray(),
                        LdFolder
                    );
                    var candidates = queued
                        .Select(t => t.FilePath)
                        .Where(f => IsPFlowMedia(f, type))
                        .ToList();
                    if (candidates.Count == 0)
                        continue;
                    if (page.AppClone != "FB-01")
                        throw new InvalidOperationException(
                            "P-Flow supports FB-01; select a mapped package before posting clones."
                        );
                    if (
                        string.IsNullOrWhiteSpace(page.PageId)
                        || string.IsNullOrWhiteSpace(page.UserUid)
                        || string.IsNullOrWhiteSpace(page.PageName)
                    )
                        throw new InvalidOperationException(
                            $"{page.DeviceName}: fetch a complete target identity before posting."
                        );
                    if (
                        devicePages.Count(p =>
                            p.UserUid == page.UserUid
                            && p.PageName.Equals(page.PageName, StringComparison.OrdinalIgnoreCase)
                        ) > 1
                    )
                        throw new InvalidOperationException(
                            "Duplicate target names require identity disambiguation before UI posting."
                        );
                    var instanceIdentity = PFlowDeviceRouting.ReadIdentity(LdFolder, worker.Index);
                    var binding =
                        targetModel.AccountBinding
                        ?? new PFlowAccountBinding(
                            instanceIdentity,
                            worker.Index,
                            worker.Name,
                            "com.facebook.katana",
                            page.UserUid
                        );
                    binding.RequireWorker(
                        worker.Index,
                        worker.Name,
                        instanceIdentity,
                        "com.facebook.katana"
                    );
                    if (binding.OwnerUid != page.UserUid)
                        throw new InvalidOperationException(
                            "Target owner UID differs from its saved account binding."
                        );
                    targetModel.AccountBinding = binding;
                    if (account.Binding != binding)
                        throw new InvalidOperationException(
                            "Target differs from its assigned personal account binding."
                        );
                    foreach (var task in queued)
                    {
                        var file = task.FilePath;
                        if (!IsPFlowMedia(file, type))
                            continue;
                        var job = new PFlowJob
                        {
                            AccountBinding = binding,
                            BatchId = batch,
                            DeviceIndex = worker.Index,
                            TargetId = page.PageId,
                            TargetName = page.PageName,
                            OwnerUid = page.UserUid,
                            PersonaUid = page.PersonaUid,
                            IsProfile = page.IsProfile,
                            SourcePath = file,
                            PartGroupKey = task.PartGroupKey,
                            PartNumber = task.PartNumber,
                            PostType = type,
                            Caption =
                                type == "Story" ? ""
                                : task.Title is { Length: > 0 } ? task.Title
                                : ResolveCaptionForMedia(
                                    file,
                                    type == "Picture" ? page.PhotoTitleFromTextFile
                                        : type == "Video" ? page.VideoTitleFromTextFile
                                        : page.ReelTitleFromTextFile
                                ),
                            BackupRequested =
                                type == "Story" ? config.BackupStoryFile
                                : type == "Reel" ? config.BackupReelFile
                                : type == "Picture" ? config.BackupPictureFile
                                : config.BackupVideoFile,
                            DeleteRequested =
                                type == "Story" ? config.DeleteStoryFile
                                : type == "Reel" ? config.DeleteReelFile
                                : type == "Picture" ? config.DeletePictureFile
                                : config.DeleteVideoFile,
                        };
                        await PFlowJob.FingerprintAsync(job, CancellationToken.None);
                        if (history.Any(h => h.DuplicateKey == job.DuplicateKey && !h.CanRetry))
                        {
                            task.Status = "Skipped (P-Flow history)";
                            continue;
                        }
                        if (_pFlowJobs.Any(j => j.DuplicateKey == job.DuplicateKey))
                        {
                            task.Status = "Skipped (Duplicate file already queued for this target)";
                            continue;
                        }
                        if (uniqueAcrossTargets && _pFlowJobs.Any(j => j.Sha256 == job.Sha256))
                        {
                            task.Status = "Skipped (Unique file)";
                            continue;
                        }
                        job.State = PFlowState.Queued;
                        if (!await _pFlowHistory.ReserveAsync(job))
                        {
                            task.Status = "Skipped (Reserved elsewhere)";
                            continue;
                        }
                        task.PFlowJobId = job.JobId;
                        _pFlowJobs.Add(job);
                        _pFlowTasks[job.JobId] = task;
                    }
                    if (page.Model != null)
                    {
                        page.Model.PostsTodayCount = await _pFlowHistory.DailyCountAsync(
                            page.PageId,
                            page.GetPostsToday()
                        );
                        page.Model.LastPostDate = DateTime.Today.ToString("yyyy-MM-dd");
                        page.RefreshMaturityStatus();
                    }
                }
            }
        }
        // Ensure unmatched legacy queue rows cannot execute via name-only virtual targets.
        foreach (
            var task in Tasks.Where(t =>
                t.Status == "Queued"
                && selectedDeviceIndices.Contains(ResolveDeviceIndex(t.DeviceName))
                && !_pFlowTasks.Values.Contains(t)
            )
        )
            task.Status = "Skipped (No verified target/file job)";
    }

    private async Task CleanupConfirmedPostsForWorkerAsync(int deviceIndex, CancellationToken ct)
    {
        var history = await _pFlowHistory.ReadAsync(ct);
        foreach (
            var job in history.Where(j =>
                j.DeviceIndex == deviceIndex
                && j.State == PFlowState.Confirmed
                && j.RemoteCleanupState != "Verified"
            )
        )
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            await new PFlowRunner(_ldPlayerService, _pFlowHistory, _atxClient).CleanupRemoteAsync(
                job,
                LdFolder,
                deadline.Token
            );
            await _pFlowHistory.SaveAsync(job, CancellationToken.None);
            ct.ThrowIfCancellationRequested();
            if (job.RemoteCleanupState != "Verified")
                throw new InvalidOperationException(
                    $"LD-{deviceIndex}: confirmed post cleanup is pending. {job.Error}"
                );
        }
    }

    private static bool IsPFlowMedia(string path, string type) =>
        (
            type == "Story" ? FacebookPageTarget.StoryExtensions
            : type == "Picture" ? new[] { ".jpg", ".jpeg", ".png", ".webp" }
            : type == "Reel" ? FacebookPageTarget.ReelExtensions
            : new[] { ".mp4", ".mkv", ".mov" }
        ).Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private async Task ExecutePFlowForPageAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        string type,
        CancellationToken ct
    )
    {
        var runner = new PFlowRunner(_ldPlayerService, _pFlowHistory, _atxClient);
        foreach (
            var job in _pFlowJobs
                .Where(j =>
                    j.DeviceIndex == worker.Index
                    && j.OwnerUid == page.UserUid
                    && j.AppPackage == "com.facebook.katana"
                    && j.TargetId == page.PageId
                    && j.IsProfile == page.IsProfile
                    && j.PostType == type
                    && j.State == PFlowState.Queued
                )
                .ToList()
        )
        {
            ct.ThrowIfCancellationRequested();
            RequireSubscription();
            if (job.State != PFlowState.Queued)
                continue;
            _pFlowTasks.TryGetValue(job.JobId, out var task);
            var records = await _pFlowHistory.ReadAsync(ct);
            var partBlock = PFlowPartGroups.BlockingReason(job, records);
            if (partBlock.Length > 0)
            {
                worker.Status = $"P-Flow [{page.PageName}]: {partBlock}";
                if (task != null)
                    task.Status = "Held (Part order): " + partBlock;
                // Leave later parts queued; the earlier part must be resolved first.
                break;
            }
            int uncertain = records.Count(j =>
                j.TargetId == page.PageId
                && j.State
                    is PFlowState.Unknown
                        or PFlowState.SubmitRequested
                        or PFlowState.Submitted
            );
            if (
                AutoMaxPostByMaturity
                && page.GetPostsToday() + uncertain >= page.GetMaxDailyPostsLimit()
            )
            {
                if (task != null)
                    task.Status = "Skipped (Daily capacity)";
                continue;
            }
            async Task<bool> RunJob() =>
                await runner.RunAsync(
                    job,
                    c => VerifyPFlowIdentityAsync(worker, page, c, job),
                    async step =>
                    {
                        var cancelled = PFlowPageIdentityRestriction.CancelQueued(job, _pFlowJobs);
                        foreach (var pending in cancelled)
                            await _pFlowHistory.SaveAsync(pending, CancellationToken.None);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            foreach (var pending in cancelled)
                                if (_pFlowTasks.TryGetValue(pending.JobId, out var pendingTask))
                                    pendingTask.Status = pending.CurrentStep;
                        });
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            worker.Status = $"P-Flow [{page.PageName}] {step}";
                            if (task != null)
                                task.Status = step;
                        });
                    },
                    LdFolder,
                    ct,
                    _pFlowBatchDryRun,
                    AutoMaxPostByMaturity ? page.GetMaxDailyPostsLimit() : int.MaxValue
                );
            bool confirmed = await RunJob();
            if (
                job.State == PFlowState.Failed
                && !job.TargetIdentityConfirmationRequired
                && !job.SubmittedUtc.HasValue
                && !ct.IsCancellationRequested
            )
            {
                var authentication = PFlowLoginScreen.Classify(
                    await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct)
                );
                if (authentication == PFlowLoginScreenKind.LoginRequired)
                {
                    var account = _pFlowRunAccounts.Single(a => a.Binding == job.AccountBinding);
                    await EnsureRunAccountAsync(worker, account, ct);
                    job.Error = "";
                    job.RemoteCleanupState = "Pending";
                    confirmed = await RunJob();
                }
                else if (PFlowLoginScreen.RequiresAuthentication(authentication))
                {
                    _pFlowAccountAttention[worker.Index] = 0;
                    throw new InvalidOperationException(PFlowLoginScreen.Message(authentication));
                }
            }
            if (confirmed && job.RemoteCleanupState != "Verified")
                throw new InvalidOperationException(
                    "Post confirmed, but emulator media / album cleanup is pending. Remaining queued posts were stopped."
                );
            if (job.TargetIdentityConfirmationRequired || job.State == PFlowState.Unknown)
                InvalidatePostTargetSession(worker.Index);
            await RefreshMediaCountsForPagesAsync(new[] { page });
            if (_pFlowAccountAttention.ContainsKey(worker.Index))
                throw new InvalidOperationException(
                    "Account authentication needs attention; remaining tasks on this LD were stopped."
                );
            if (confirmed)
            {
                var count = await _pFlowHistory.DailyCountAsync(
                    page.PageId,
                    page.GetPostsToday(),
                    CancellationToken.None
                );
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    page.Model.PostsTodayCount = count;
                    page.Model.LastPostDate = DateTime.Today.ToString("yyyy-MM-dd");
                    page.RefreshMaturityStatus();
                });
                await PersistPagesSnapshotAsync();
            }
            if (
                job.TargetIdentityConfirmationRequired
                && !await PFlowPageIdentityRestriction.DismissAsync(
                    _ldPlayerService,
                    worker.Index,
                    LdFolder,
                    ct
                )
            )
                break;
            if (
                job.State == PFlowState.Failed
                && !job.SubmittedUtc.HasValue
                && !ct.IsCancellationRequested
            )
            {
                // Do not carry a failed caption editor or stale navigation into the next target.
                worker.Status = "P-Flow: resetting failed pre-submit navigation";
                _pFlowOwners.TryRemove(worker.Index, out _);
                _pFlowActiveBindings.TryRemove(worker.Index, out _);
                await _ldPlayerService.StopFacebookAppAsync(worker.Index, LdFolder, ct);
                await _ldPlayerService.LaunchAppAsync(
                    worker.Index,
                    "com.facebook.katana",
                    LdFolder,
                    ct
                );
            }
        }
    }

    private async Task<bool> VerifyPFlowIdentityAsync(
        LDInstanceItemViewModel worker,
        FacebookPageTargetViewModel page,
        CancellationToken ct,
        PFlowJob? expectedJob = null
    )
    {
        var plannedTarget = page.Model.CreateSnapshot();
        if (
            expectedJob != null
            && (
                plannedTarget.PageId != expectedJob.TargetId
                || plannedTarget.UserUid != expectedJob.OwnerUid
                || plannedTarget.PersonaUid != expectedJob.PersonaUid
                || plannedTarget.IsPrimaryProfile != expectedJob.IsProfile
                || plannedTarget.PageName != expectedJob.TargetName
            )
        )
            throw new InvalidOperationException(
                "Target mapping changed after queue preparation. Rebuild the queue before starting work."
            );
        var account =
            _pFlowRunAccounts.SingleOrDefault(a =>
                a.Binding.DeviceIndex == worker.Index
                && a.Binding.OwnerUid == page.UserUid
                && a.Binding.AppPackage == "com.facebook.katana"
            )
            ?? throw new InvalidOperationException(
                "Target has no account in the immutable run plan."
            );
        if (expectedJob != null)
        {
            try
            {
                await CheckPostProcessAsync(worker.Index, ct);
            }
            catch
            {
                InvalidatePostTargetSession(worker.Index);
                throw;
            }
        }
        await CreateRunSessionPreparation()
            .RestorePostForegroundFromLauncherAsync(account.Binding, ct);
        string? initialXml = await _ldPlayerService.DumpUiHierarchyAsync(
            worker.Index,
            LdFolder,
            ct
        );
        initialXml = await RecoverRunStartingScreenAsync(
            account.Binding,
            plannedTarget.PageName,
            expectedJob,
            initialXml,
            ct
        );
        var sessionScreen = PFlowLoginScreen.Classify(initialXml);
        if (
            PFlowLoginScreen.RequiresAuthentication(sessionScreen)
            && sessionScreen != PFlowLoginScreenKind.LoginRequired
        )
        {
            _pFlowAccountAttention[worker.Index] = 0;
            throw new InvalidOperationException(PFlowLoginScreen.Message(sessionScreen));
        }
        if (
            sessionScreen == PFlowLoginScreenKind.LoginRequired
            || !_pFlowActiveBindings.TryGetValue(worker.Index, out var verifiedBinding)
            || verifiedBinding != account.Binding
        )
        {
            await EnsureRunAccountAsync(worker, account, ct);
            initialXml = null; // Account preparation can change the UI.
        }
        // Both ScreenTime's startup callback and every PFlow job enter here.
        // Posting can reuse a confirmed target session; other callers verify fresh UID.
        var navigationRecoveryAttempted = false;
        try
        {
            return await PFlowTargetNavigationRecovery.RunAsync(
                expectedJob,
                () =>
                    VerifyLiveTargetUidAsync(
                        worker,
                        page,
                        plannedTarget,
                        account,
                        ct,
                        postingJob: expectedJob,
                        initialXml: initialXml
                    ),
                async () =>
                {
                    navigationRecoveryAttempted = true;
                    RequireSubscription();
                    if (
                        PFlowJobStartRecovery.HasPendingPublication(
                            (await _pFlowHistory.ReadAsync(ct)).Concat(_pFlowJobs),
                            worker.Index
                        )
                    )
                        throw new InvalidOperationException(
                            "Pending publication blocks navigation recovery."
                        );
                    await RequireAccountTransitionIdleAsync(worker.Index, ct);
                    var recoveryXml =
                        await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct)
                        ?? "";
                    if (!PFlowTargetNavigationRecovery.IsSafeFeed(recoveryXml, plannedTarget))
                        throw new InvalidOperationException(
                            "Navigation recovery requires a safe Facebook Feed."
                        );
                    worker.Status = "P-Flow: restarting navigation and verifying account";
                    InvalidatePostTargetSession(worker.Index);
                    _pFlowOwners.TryRemove(worker.Index, out _);
                    _pFlowActiveBindings.TryRemove(worker.Index, out _);
                    await PFlowTrace.SessionAsync(
                        worker.Index,
                        "identity.navigation.recovery",
                        "attempt=1; submitted=false; freshGraphRequired=true",
                        ct
                    );
                    await _ldPlayerService.StopFacebookAppAsync(worker.Index, LdFolder, ct);
                    await _ldPlayerService.LaunchAppAsync(
                        worker.Index,
                        "com.facebook.katana",
                        LdFolder,
                        ct
                    );
                    initialXml = null;
                    try
                    {
                        await EnsureRunAccountAsync(worker, account, ct);
                    }
                    catch
                    {
                        _pFlowAccountAttention[worker.Index] = 0;
                        throw;
                    }
                },
                ct
            );
        }
        catch
        {
            if (navigationRecoveryAttempted)
                _pFlowAccountAttention[worker.Index] = 0;
            InvalidatePostTargetSession(worker.Index);
            _pFlowOwners.TryRemove(worker.Index, out _);
            _pFlowActiveBindings.TryRemove(worker.Index, out _);
            throw;
        }
    }

    private async Task FinishPFlowFilesAsync()
    {
        var runner = new PFlowRunner(_ldPlayerService, _pFlowHistory, _atxClient);
        foreach (var job in _pFlowJobs.Where(j => j.State == PFlowState.Queued))
        {
            job.State = PFlowState.CancelledBeforeSubmission;
            job.Error = "Batch ended before this job ran.";
            await _pFlowHistory.SaveAsync(job);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_pFlowTasks.TryGetValue(job.JobId, out var task))
                    task.Status = "Cancelled before submission (batch stopped)";
            });
        }
        foreach (var job in _pFlowJobs.Where(j => j.State == PFlowState.Confirmed))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await runner.DeleteSharedSourceAsync(job, _pFlowJobs, timeout.Token);
        }
        await RefreshMediaCountsForPagesAsync(
            await Dispatcher.UIThread.InvokeAsync(() => Pages.ToArray())
        );
    }

    [RelayCommand]
    private async Task ShowPFlowHistoryAsync()
    {
        var window = new _855Media.Views.Components.PFlowHistoryWindow(
            _pFlowHistory,
            _ldPlayerService,
            _atxClient,
            LdFolder,
            () => IsRunning || IsBusy,
            async () =>
            {
                await ReconcileSavedPostQueueAsync();
                await RefreshMediaCountsForPagesAsync(Pages.ToArray());
            }
        );
        await window.RefreshAsync();
        if (
            Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null
        )
            window.Show(desktop.MainWindow);
        else
            window.Show();
    }

    private static string ResolveCaptionForMedia(string mediaFile, bool useTitleFromTextFile) =>
        PFlowMediaCaption.Resolve(mediaFile, useTitleFromTextFile);
}
