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
        await LoadAccountMappingsAsync();
        _pFlowJobs.Clear();
        _pFlowTasks.Clear();
        _pFlowOwners.Clear();
        _pFlowBatchDryRun = PFlowDryRun;
        var uniqueAcrossTargets = UploadUniqueFile;
        var selectedWorkers = (ShowOnlyOpen ? DisplayedInstances : Instances)
            .Where(w => w.IsSelected)
            .ToList();
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
        if (
            history.Any(j =>
                j.State == PFlowState.Unknown && selectedWorkers.Any(w => w.Index == j.DeviceIndex)
            )
        )
            throw new InvalidOperationException(
                "Review Unknown jobs in P-Flow History before reusing that LD instance."
            );
        if (config.ScreenTimeOnly)
            return;
        var batch = Guid.NewGuid().ToString("N");
        foreach (var worker in selectedWorkers)
        {
            var devicePages = Pages
                .Where(p => ResolveDeviceIndex(p.DeviceName) == worker.Index)
                .ToList();
            PFlowAccountSelection.RequireSupportedSingleLogin(
                _mappedAccounts,
                worker.Index,
                "com.facebook.katana",
                devicePages.Where(p => p.IsProfile).Select(p => p.UserUid)
            );
            if (devicePages.Where(p => p.IsProfile).Select(p => p.UserUid).Distinct().Count() > 1)
                throw new InvalidOperationException(
                    "Multiple personal logins need verified saved-account switching. No account was selected by guessing."
                );
            foreach (var page in devicePages)
            {
                if (!page.IsProfile && page.Model?.LivePageAccessVerified == false)
                {
                    foreach (
                        var task in Tasks.Where(t =>
                            t.Status == "Queued"
                            && ResolveDeviceIndex(t.DeviceName) == worker.Index
                            && t.PageId == page.PageId
                        )
                    )
                        task.Status = "Blocked: Page access not verified; fetch its account again.";
                    continue;
                }

                var account = _mappedAccounts.SingleOrDefault(a =>
                    a.Binding.DeviceIndex == worker.Index
                    && a.Binding.OwnerUid == page.UserUid
                    && a.Binding.AppPackage == "com.facebook.katana"
                );
                if (account == null || !account.SelectedForPosting)
                    continue;
                foreach (var type in new[] { "Reel", "Picture", "Video" })
                {
                    if (
                        !(
                            type == "Reel" ? config.PostReel
                            : type == "Picture" ? config.PostPicture
                            : config.PostVideo
                        )
                    )
                        continue;
                    var queued = Tasks
                        .Where(t =>
                            t.Status == "Queued"
                            && ResolveDeviceIndex(t.DeviceName) == worker.Index
                            && (t.Type == type || type == "Picture" && t.Type == "Photo")
                            && (
                                !string.IsNullOrWhiteSpace(t.PageId)
                                    ? t.PageId == page.PageId
                                    : t.Page.Equals(
                                        page.PageName,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                            )
                        )
                        .ToList();
                    if (queued.Count == 0 && !page.IsSelected)
                        continue;
                    PFlowAccountStore.RequireCurrentBinding(
                        account.Binding,
                        Instances.Select(i => i.Model).ToArray(),
                        LdFolder
                    );
                    string source = (
                        type == "Reel" ? page.VideoReelPath
                        : type == "Picture" ? page.ImagePath
                        : page.VideoLongPath
                    ).Trim().Trim('"', '\'');
                    var candidates =
                        queued.Count > 0 ? queued.Select(t => t.FilePath).ToList()
                        : File.Exists(source) ? new List<string> { source }
                        : Directory.Exists(source)
                            ? Directory
                                .EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly)
                                .Where(f => IsPFlowMedia(f, type))
                                .OrderBy(f => f)
                                .ToList()
                        : new List<string>();
                    if (
                        queued.Count == 0
                        && source.Length > 0
                        && !File.Exists(source)
                        && !Directory.Exists(source)
                    )
                        throw new IOException("Media path is missing: " + source);
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
                            p.PageName.Equals(page.PageName, StringComparison.OrdinalIgnoreCase)
                        ) > 1
                    )
                        throw new InvalidOperationException(
                            "Duplicate target names require identity disambiguation before UI posting."
                        );
                    var targetModel =
                        page.Model
                        ?? throw new InvalidOperationException("Target model is missing.");
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
                    foreach (var file in candidates)
                    {
                        if (!IsPFlowMedia(file, type))
                            continue;
                        var task = queued.FirstOrDefault(t => t.FilePath == file);
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
                            PostType = type,
                            Caption = task?.Title is { Length: > 0 }
                                ? task.Title
                                : ResolveCaptionForMedia(
                                    file,
                                    type == "Reel" ? page.TitleVideoReel
                                        : type == "Picture" ? page.TitleImage
                                        : page.TitleVideoLong,
                                    page.UseTitleFromTextFile,
                                    page.UseCaptionFromFilename
                                ),
                            BackupRequested =
                                type == "Reel" ? config.BackupReelFile
                                : type == "Picture" ? config.BackupPictureFile
                                : config.BackupVideoFile,
                            DeleteRequested =
                                type == "Reel" ? config.DeleteReelFile
                                : type == "Picture" ? config.DeletePictureFile
                                : config.DeleteVideoFile,
                        };
                        await PFlowJob.FingerprintAsync(job, CancellationToken.None);
                        if (
                            history.Any(h =>
                                h.DuplicateKey == job.DuplicateKey
                                && h.State
                                    is not (
                                        PFlowState.Failed
                                        or PFlowState.CancelledBeforeSubmission
                                    )
                            )
                        )
                        {
                            if (task != null)
                                task.Status = "Skipped (P-Flow history)";
                            continue;
                        }
                        if (_pFlowJobs.Any(j => j.DuplicateKey == job.DuplicateKey))
                            continue;
                        if (uniqueAcrossTargets && _pFlowJobs.Any(j => j.Sha256 == job.Sha256))
                        {
                            if (task != null)
                                task.Status = "Skipped (Unique file)";
                            continue;
                        }
                        job.State = PFlowState.Queued;
                        if (!await _pFlowHistory.ReserveAsync(job))
                        {
                            if (task != null)
                                task.Status = "Skipped (Reserved elsewhere)";
                            continue;
                        }
                        _pFlowJobs.Add(job);
                        if (task != null)
                            _pFlowTasks[job.JobId] = task;
                        if (queued.Count == 0)
                            break; // Legacy folder mode posts one eligible file per enabled type.
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
            var task in Tasks.Where(t => t.Status == "Queued" && !_pFlowTasks.Values.Contains(t))
        )
            task.Status = "Skipped (No verified target/file job)";
    }

    private static bool IsPFlowMedia(string path, string type) =>
        (
            type == "Picture" ? new[] { ".jpg", ".jpeg", ".png", ".webp" }
            : type == "Reel" ? new[] { ".mp4", ".mov", ".webm" }
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
                    && j.PostType == type
                    && j.State == PFlowState.Queued
                )
                .ToList()
        )
        {
            ct.ThrowIfCancellationRequested();
            _pFlowTasks.TryGetValue(job.JobId, out var task);
            var records = await _pFlowHistory.ReadAsync(ct);
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
            bool confirmed = await runner.RunAsync(
                job,
                c => VerifyPFlowIdentityAsync(worker, page, c),
                async step =>
                {
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
            if (job.State == PFlowState.Unknown)
                break; // Leave the unresolved composer/uploader for review.
            if (
                job.State == PFlowState.Failed
                && !job.SubmittedUtc.HasValue
                && !ct.IsCancellationRequested
            )
            {
                // Do not carry a failed caption editor or stale navigation into the next target.
                worker.Status = "P-Flow: resetting failed pre-submit navigation";
                _pFlowOwners.TryRemove(worker.Index, out _);
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
        CancellationToken ct
    )
    {
        var sessionScreen = PFlowLoginScreen.Classify(
            await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct)
        );
        if (PFlowLoginScreen.RequiresAuthentication(sessionScreen))
            throw new InvalidOperationException(PFlowLoginScreen.Message(sessionScreen));
        var personalSelected = false;
        if (!_pFlowOwners.TryGetValue(worker.Index, out var owner) || owner != page.UserUid)
        {
            worker.Status = "P-Flow: verifying root owner and managed target IDs";
            var root = await _ldPlayerService.ExtractFacebookAccountsFromRootShellAsync(
                worker.Index,
                "com.facebook.katana",
                LdFolder,
                ct
            );
            if (!root.Any(a => a.Type == "Profile" && a.Uid == page.UserUid))
                return false;
            if (
                !page.IsProfile
                && !root.Any(a =>
                    a.Type == "Page" && a.PageId == page.PageId && a.UserUid == page.UserUid
                )
            )
                return false;
            var profile = Pages.SingleOrDefault(p =>
                ResolveDeviceIndex(p.DeviceName) == worker.Index
                && p.IsProfile
                && p.UserUid == page.UserUid
            );
            if (profile == null)
                return false;
            worker.Status = $"P-Flow: selecting owner profile [{profile.PageName}]";
            if (
                !await _ldPlayerService.EnsureSwitchedToPersonalProfileAsync(
                    worker.Index,
                    profile.PageName,
                    LdFolder,
                    ct
                )
            )
                return false;
            _pFlowOwners[worker.Index] = page.UserUid;
            personalSelected = page.IsProfile;
        }
        // The shared switcher owns recovery. A personal target already selected above
        // needs a fresh identity check, not another trip through the dropdown.
        worker.Status = $"P-Flow: confirming [{page.PageName}]";
        if (!personalSelected && !await EnsureTargetPersonaActiveAsync(worker, page, ct))
        {
            _pFlowOwners.TryRemove(worker.Index, out _);
            return false;
        }
        var xml = await _ldPlayerService.DumpUiHierarchyAsync(worker.Index, LdFolder, ct) ?? "";
        bool own = page.IsProfile
            ? xml.Contains("Edit profile", StringComparison.OrdinalIgnoreCase)
            : xml.Contains("Professional dashboard", StringComparison.OrdinalIgnoreCase)
                || xml.Contains("Manage Page", StringComparison.OrdinalIgnoreCase);
        var confirmed =
            own
            && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            && !xml.Contains("Admin quick access", StringComparison.OrdinalIgnoreCase)
            && PFlowAutoPostService.ComposerMatchesTarget(xml, page.PageName);
        if (!confirmed)
            _pFlowOwners.TryRemove(worker.Index, out _);
        return confirmed;
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
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var page in Pages)
                page.RefreshMediaCounts();
        });
    }

    [RelayCommand]
    private async Task ShowPFlowHistoryAsync()
    {
        var window = new _855Media.Views.Components.PFlowHistoryWindow(
            _pFlowHistory,
            _ldPlayerService,
            _atxClient,
            LdFolder,
            () => IsRunning || IsBusy
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

    private static string ResolveCaptionForMedia(
        string mediaFile,
        string directCaption,
        bool useTitleFromTextFile,
        bool useCaptionFromFilename = false
    )
    {
        if (useTitleFromTextFile)
        {
            try
            {
                var txtPath = Path.ChangeExtension(mediaFile, ".txt");
                if (File.Exists(txtPath))
                {
                    var text = File.ReadAllText(txtPath).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
            catch { }
        }

        return useCaptionFromFilename
            ? Path.GetFileNameWithoutExtension(mediaFile)
            : directCaption ?? string.Empty;
    }
}
