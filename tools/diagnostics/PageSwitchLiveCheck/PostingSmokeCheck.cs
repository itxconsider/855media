using System.Reflection;
using System.Text.Json;
using _855Media.Core.LDPlayer;

internal static class PostingSmokeCheck
{
    internal static async Task RunAsync(string[] args)
    {
        var folder =
            args.FirstOrDefault(a => a.StartsWith("--folder="))?[9..] ?? @"C:\LDPlayer\LDPlayer9";
        var output = Path.GetFullPath(
            args.FirstOrDefault(a => a.StartsWith("--output="))?[9..]
                ?? "artifacts/posting-live-smoke"
        );
        var source = Path.GetFullPath(args.First(a => a.StartsWith("--media="))[8..]);
        var story = args.Contains("--story");
        var photoCount = int.Parse(
            args.FirstOrDefault(a => a.StartsWith("--photos="))?[9..] ?? "0"
        );
        if (photoCount < 0 || photoCount > 10 || story && photoCount != 0)
            throw new ArgumentOutOfRangeException(nameof(photoCount));
        var only = args.FirstOrDefault(a => a.StartsWith("--only="))?[7..];
        Directory.CreateDirectory(output);
        using var atxRoot = new AtxAgentClient();
        var atx = atxRoot.ForInstallation(folder);
        var real = new LDPlayerService(atx);
        using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
        var installationData = new PFlowInstallationData(folder);
        var scoped = Directory.Exists(installationData.DirectoryPath);
        var targets = (
            await PFlowPageStorage.LoadPagesAsync(
                scoped ? installationData.FilePath("facebook_pages.json") : null
            )
        ).ToArray();
        if (args.Contains("--activation-probe"))
        {
            var ix = int.Parse(only!);
            var saved = targets.Where(t => t.DeviceName == "LD-" + ix).ToArray();
            var target = saved.First(t => !t.IsPrimaryProfile);
            var binding = saved.Single(t => t.IsPrimaryProfile).AccountBinding!;
            var process = (
                await real.ExecuteAdbAsync(ix, "shell pidof com.facebook.katana", folder)
            ).Trim();
            var proof = (PFlowNativeIdentityScope?)
                typeof(PFlowNativeIdentityScope)
                    .GetMethod("FromSelectedName", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(
                        null,
                        [
                            binding,
                            process,
                            target,
                            new PFlowPostingNameVerification.Actor(target.PageName, target.Type),
                            saved,
                        ]
                    );
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        process,
                        activity = await real.GetCurrentFocusedActivityAsync(ix, folder),
                        savedMatch = PFlowPostingNameVerification.MatchesSavedTarget(target, saved),
                        proofCreated = proof != null,
                        processMatches = proof != null
                            && await proof.SessionUnchangedAsync(real, folder, default),
                    }
                )
            );
            return;
        }
        var running = (await real.GetInstancesAsync(folder))
            .Where(d =>
                d.Index != 56 && d.IsAndroidStarted && (only == null || d.Index.ToString() == only)
            )
            .ToArray();
        var results = new List<object>();
        foreach (var device in running)
        {
            var index = device.Index;
            var directory = Path.Combine(output, "LD-" + index);
            Directory.CreateDirectory(directory);
            var checks = new List<object>();
            var ld = DispatchProxy.Create<ILDPlayerService, NoPublishDevice>();
            var guard = (NoPublishDevice)(object)ld;
            guard.Real = real;
            guard.Path = folder;
            FacebookPageTarget? original = null;
            PFlowJob? job = null;
            var ownsDraft = false;
            IDisposable? lease = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var ct = timeout.Token;
            async Task Capture(string label)
            {
                var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                if (ownsDraft)
                {
                    var field = ScreenTimeHierarchy
                        .Nodes(xml)
                        .FirstOrDefault(n =>
                            n.Attribute("class")?.Value == "android.widget.EditText"
                        );
                    var text = field?.Attribute("text")?.Value ?? "";
                    Console.WriteLine(
                        $"LD-{index} CAPTURE {label}: entry={FacebookLayoutRegistry.Match(xml, FacebookScreenKind.PhotoPostEntry).Diagnostic}; captionChars={text.Length}; placeholder={(text.Contains("mind", StringComparison.OrdinalIgnoreCase) ? text : "[other]")}"
                    );
                }
                await File.WriteAllTextAsync(
                    Path.Combine(directory, label + ".xml"),
                    PFlowUiPrivacy.RedactForDiagnostics(xml),
                    ct
                );
                if (
                    !PFlowUiPrivacy.ContainsInput(xml)
                    && await ld.TakeScreenshotAsync(index, folder, ct) is { } png
                )
                    await File.WriteAllBytesAsync(Path.Combine(directory, label + ".png"), png, ct);
            }
            try
            {
                using var leaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                lease = await PFlowDeviceLease.AcquireAsync(index, leaseTimeout.Token, folder);
                var history = await new PFlowHistoryStore(
                    scoped ? installationData.FilePath("facebook_post_history.db") : null
                ).ReadAsync(ct);
                if (PFlowJobStartRecovery.HasPendingPublication(history, index))
                    throw new Exception("Pending publication blocks test");
                var notifications = await ld.ExecuteAdbAsync(
                    index,
                    "shell dumpsys notification --noredact",
                    folder,
                    ct
                );
                if (PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing))
                    throw new Exception("Active upload blocks test");
                if (!await ld.EnsureAtxAgentRunningAsync(index, folder, ct))
                    throw new Exception("ATX unavailable");
                var identity = PFlowDeviceRouting.ReadIdentity(folder, index);
                var saved = targets
                    .Where(t =>
                        t.DeviceName == device.Name
                        && t.AccountBinding?.InstanceIdentity == identity
                    )
                    .ToArray();
                var personal = saved.Single(t => t.IsPrimaryProfile);
                var binding =
                    personal.AccountBinding ?? throw new Exception("Saved binding missing");
                await Capture("initial");
                var xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                for (var read = 0; !xml.Contains("<node") && read < 5; read++)
                {
                    await Task.Delay(700, ct);
                    xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                }
                if (!xml.Contains("<node"))
                    throw new Exception(
                        "UI inspection returned an empty hierarchy; no draft or identity could be inspected"
                    );
                if (
                    args.Contains("--close-empty-picker")
                    && xml.Contains("New post")
                    && xml.Contains("Select multiple")
                    && !xml.Contains("EditText")
                    && !xml.Contains("SubmitRequested")
                )
                {
                    await ld.PressBackAsync(index, folder, ct);
                    await Task.Delay(500, ct);
                    xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                }
                if (
                    PFlowUiPrivacy.ContainsInput(xml)
                    || new[]
                    {
                        FacebookScreenKind.ReelComposer,
                        FacebookScreenKind.PhotoComposer,
                        FacebookScreenKind.StoryEditor,
                        FacebookScreenKind.PhotoPicker,
                        FacebookScreenKind.PhotoPostEntry,
                    }.Any(kind => FacebookLayoutRegistry.Match(xml, kind).IsMatch)
                )
                    throw new Exception("Existing draft/input: left untouched");
                if (
                    !(await ld.GetCurrentFocusedActivityAsync(index, folder, ct) ?? "").Contains(
                        "com.facebook.katana"
                    )
                )
                {
                    if (!await ld.LaunchAppAsync(index, "com.facebook.katana", folder, ct))
                        throw new Exception("Launch failed");
                    await Task.Delay(1500, ct);
                    xml = await ld.DumpUiHierarchyAsync(index, folder, ct) ?? "";
                    if (
                        PFlowUiPrivacy.ContainsInput(xml)
                        || FacebookLayoutRegistry
                            .Match(xml, FacebookScreenKind.ReelComposer)
                            .IsMatch
                    )
                        throw new Exception("Facebook reopened an existing draft: left untouched");
                }
                if (PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml)))
                    throw new Exception("Authentication prompt: left untouched");
                // Previous dry-run restoration can leave a read-only Dashboard.
                // Normalize only a recognized browse/profile destination before observing its actor.
                if (
                    new[]
                    {
                        FacebookScreenKind.Dashboard,
                        FacebookScreenKind.Analytics,
                        FacebookScreenKind.OwnProfile,
                    }.Any(kind => FacebookLayoutRegistry.Match(xml, kind).IsMatch)
                )
                {
                    await ld.ExecuteAdbAsync(
                        index,
                        "shell am start -a android.intent.action.VIEW -d 'fb://feed' -p com.facebook.katana",
                        folder,
                        ct
                    );
                    await Task.Delay(1000, ct);
                }
                var active =
                    await PFlowPostingNameVerification.ObserveAccountAsync(
                        ld,
                        binding,
                        personal.PageName,
                        folder,
                        ct,
                        saved
                    ) ?? throw new Exception("Could not identify account/active actor");
                original =
                    saved.SingleOrDefault(t =>
                        t.Type == active.Type && Same(t.PageName, active.Name)
                    ) ?? throw new Exception("Active actor has no unique saved mapping");
                var sequence = saved.Where(t => !t.IsPrimaryProfile).Concat([personal]).ToArray();
                foreach (
                    var target in args.Contains("--current-actor")
                        ? Array.Empty<FacebookPageTarget>()
                        : sequence
                )
                {
                    var start = DateTime.UtcNow;
                    try
                    {
                        var scope = await PFlowPostingNameVerification.ActivateAsync(
                            ld,
                            binding,
                            target,
                            saved,
                            folder,
                            ct
                        );
                        if (!await scope.VerifyAsync(ld, folder, ct))
                            throw new Exception("Repeat proof failed");
                        checks.Add(
                            new
                            {
                                kind = "switch",
                                target = target.PageName,
                                passed = true,
                                seconds = (DateTime.UtcNow - start).TotalSeconds,
                            }
                        );
                        Console.WriteLine($"LD-{index} SWITCH PASS {target.PageName}");
                    }
                    catch (Exception ex)
                    {
                        checks.Add(
                            new
                            {
                                kind = "switch",
                                target = target.PageName,
                                passed = false,
                                error = ex.Message,
                            }
                        );
                        await Capture("switch-failure");
                        throw;
                    }
                }
                if (args.Contains("--screentime"))
                {
                    var settings = new ScreenTimeSetting
                    {
                        OwnerUid = binding.OwnerUid,
                        OwnerName = personal.PageName,
                        NewsFeed = true,
                        NewsFeedMin = 8,
                        NewsFeedMax = 8,
                        WatchVideo = true,
                        WatchVideoMin = 8,
                        WatchVideoMax = 8,
                        Marketplace = true,
                        MarketplaceMin = 3,
                        MarketplaceMax = 3,
                        ReadNotification = true,
                        ClearRecentApp = false,
                        LikePost = false,
                        Options = new ScreenTimeOptions
                        {
                            Stories = true,
                            StoryLimit = 1,
                            StoryDwellSeconds = 3,
                            MaxSessionSeconds = 240,
                            RandomFeedReactions = false,
                            RandomFeedComments = false,
                            Reactions = false,
                            Comments = false,
                        },
                    };
                    var service = (ScreenTimeService)
                        Activator.CreateInstance(
                            typeof(ScreenTimeService),
                            BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            new object?[]
                            {
                                ld,
                                (Func<TimeSpan, CancellationToken, Task>)Task.Delay,
                                (Func<DateTimeOffset>)(() => DateTimeOffset.UtcNow),
                                new ScreenTimeHistoryStore(
                                    Path.Combine(directory, "screentime.db")
                                ),
                                (Func<int>)(() => 0),
                            },
                            null
                        )!;
                    var result = await service.RunAsync(
                        index,
                        settings,
                        async token =>
                        {
                            await PFlowPostingNameVerification.ActivateAsync(
                                ld,
                                binding,
                                personal,
                                saved,
                                folder,
                                token
                            );
                            return true;
                        },
                        progress =>
                        {
                            Console.WriteLine(JsonSerializer.Serialize(progress));
                            return Task.CompletedTask;
                        },
                        folder,
                        ct,
                        deviceLeaseAlreadyHeld: true
                    );
                    checks.Add(
                        new
                        {
                            kind = "screentime",
                            result,
                            passed = result.Skipped == 0,
                        }
                    );
                    await Capture("screentime-end");
                    continue;
                }
                var postingTarget = args.Contains("--current-actor")
                    ? original
                    : saved.FirstOrDefault(t => !t.IsPrimaryProfile) ?? personal;
                var postingScope = await PFlowPostingNameVerification.ActivateAsync(
                    ld,
                    binding,
                    postingTarget,
                    saved,
                    folder,
                    ct
                );
                job = new PFlowJob
                {
                    BatchId = "diagnostic-no-share",
                    AccountBinding = binding,
                    DeviceIndex = index,
                    TargetId = postingTarget.PageId,
                    TargetName = postingTarget.PageName,
                    OwnerUid = postingTarget.UserUid,
                    PersonaUid = postingTarget.PersonaUid,
                    IsProfile = postingTarget.IsPrimaryProfile,
                    SourcePath = source,
                    PostType =
                        story ? "Story"
                        : photoCount > 0 ? "Picture"
                        : "Reel",
                    PhotoFiles =
                        photoCount > 1
                            ? Enumerable
                                .Range(1, photoCount)
                                .Select(n => new PFlowPhotoFile
                                {
                                    SourcePath = System.IO.Path.Combine(
                                        System.IO.Path.GetDirectoryName(source)!,
                                        $"photo-{n}.png"
                                    ),
                                })
                                .ToList()
                            : [],
                    NativeIdentityScope = postingScope,
                    Caption = story ? "" : "Navigation validation. Not published.",
                };
                await PFlowJob.FingerprintAsync(job, ct);
                await new PFlowPostTargetSession().VerifyAsync(
                    job,
                    _ => Task.FromResult("Database UI name"),
                    _ => throw new Exception("Unexpected own-profile identity navigation"),
                    async token => await ld.DumpUiHierarchyAsync(index, folder, token) ?? "",
                    ct
                );
                job.State = PFlowState.IdentityVerified;
                await using var keyboard = new PFlowKeyboardSession(ld, index, folder);
                await keyboard.CaptureAsync(ct);
                ownsDraft = true;
                await new PFlowAutoPostService(ld, atx).PublishAsync(
                    job,
                    async current =>
                    {
                        Console.WriteLine(
                            $"LD-{index} {current.CurrentStep}; state={current.State}"
                        );
                        if (current.MayHaveSubmitted)
                            throw new Exception("Submission boundary reached in dry run");
                        await File.WriteAllTextAsync(
                            Path.Combine(directory, "dry-run.json"),
                            JsonSerializer.Serialize(
                                current,
                                new JsonSerializerOptions { WriteIndented = true }
                            ),
                            ct
                        );
                    },
                    folder,
                    ct,
                    dryRun: true
                );
                if (
                    job.State != PFlowState.CancelledBeforeSubmission
                    || job.SubmittedUtc != null
                    || guard.PublishAttempts != 0
                )
                    throw new Exception("Dry run completion guard failed");
                checks.Add(
                    new
                    {
                        kind = "dry-run",
                        target = postingTarget.PageName,
                        passed = true,
                        mediaEvidence = job.MediaSelectionEvidence,
                    }
                );
                await Capture("ready-without-share");
                Console.WriteLine($"LD-{index} DRY RUN PASS; Share taps=0");
            }
            catch (Exception ex)
            {
                checks.Add(
                    new
                    {
                        kind = "blocked",
                        passed = false,
                        error = ex.Message,
                    }
                );
                Console.WriteLine($"LD-{index} BLOCKED {ex.Message}");
                try
                {
                    await Capture("blocked");
                }
                catch { }
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                try
                {
                    if (ownsDraft)
                    {
                        for (var back = 0; back < 8; back++)
                        {
                            var xml =
                                await ld.DumpUiHierarchyAsync(index, folder, cleanup.Token) ?? "";
                            var discard = PFlowNativeAccountNavigator.FindControl(
                                xml,
                                "Discard",
                                "Discard video",
                                "Discard post",
                                "Discard photo",
                                "Discard story",
                                "Delete draft"
                            );
                            if (discard.HasValue)
                                await ld.TapAsync(
                                    index,
                                    discard.Value.X,
                                    discard.Value.Y,
                                    folder,
                                    cleanup.Token
                                );
                            else if (
                                FacebookLayoutRegistry
                                    .Match(xml, FacebookScreenKind.NewsFeed)
                                    .IsMatch
                                || AtxAgentClient.IsMenuPageInBloks(
                                    AtxAgentClient.ParseBloksHierarchy(xml)
                                )
                            )
                                break;
                            else
                                await ld.PressBackAsync(index, folder, cleanup.Token);
                            await Task.Delay(500, cleanup.Token);
                        }
                    }
                    if (job != null && !job.MayHaveSubmitted)
                    {
                        cleanup.CancelAfter(TimeSpan.FromSeconds(45));
                        await new PFlowRunner(
                            ld,
                            new PFlowHistoryStore(Path.Combine(directory, "probe-history.db"))
                        ).CleanupRemoteAsync(job, folder, cleanup.Token);
                        await File.WriteAllTextAsync(
                            System.IO.Path.Combine(directory, "cleanup.json"),
                            JsonSerializer.Serialize(
                                new
                                {
                                    job.JobId,
                                    job.RemoteCleanupState,
                                    job.Error,
                                }
                            )
                        );
                        if (job.RemoteCleanupState != "Verified")
                            throw new Exception(
                                "Diagnostic media cleanup is still pending: " + job.Error
                            );
                    }
                    if (original != null)
                    {
                        cleanup.CancelAfter(TimeSpan.FromSeconds(45));
                        await PFlowPostingNameVerification.ActivateAsync(
                            ld,
                            original.AccountBinding!,
                            original,
                            targets.Where(t => t.AccountBinding == original.AccountBinding),
                            folder,
                            cleanup.Token
                        );
                    }
                }
                catch (Exception ex)
                {
                    checks.Add(
                        new
                        {
                            kind = "cleanup",
                            passed = false,
                            error = ex.Message,
                        }
                    );
                }
                lease?.Dispose();
                results.Add(
                    new
                    {
                        device = index,
                        name = device.Name,
                        checks,
                        publishAttempts = guard.PublishAttempts,
                    }
                );
                await File.WriteAllTextAsync(
                    Path.Combine(output, "summary.json"),
                    JsonSerializer.Serialize(
                        new { graphRequests = audit.Count, devices = results },
                        new JsonSerializerOptions { WriteIndented = true }
                    )
                );
            }
        }
    }

    private static bool Same(string a, string b) =>
        string.Equals(
            string.Concat(a.Where(c => !char.IsWhiteSpace(c))),
            string.Concat(b.Where(c => !char.IsWhiteSpace(c))),
            StringComparison.OrdinalIgnoreCase
        );

    public class NoPublishDevice : DispatchProxy
    {
        public ILDPlayerService Real = null!;
        public string Path = "";
        public int PublishAttempts;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TapAsync")
                return TapAsync(args!);
            if (
                method.Name == "ExecuteAdbAsync"
                && args![1] is string command
                && (command.Contains("authentication") || command.Contains("underlying_account"))
            )
                throw new Exception("Graph/UID credential read prohibited by smoke test");
            try
            {
                return method.Invoke(Real, args);
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private async Task<bool> TapAsync(object?[] args)
        {
            var index = (int)args[0]!;
            var x = (int)args[1]!;
            var y = (int)args[2]!;
            var ct = (CancellationToken)args[4]!;
            var xml = await Real.DumpUiHierarchyAsync(index, Path, ct) ?? "";
            foreach (var node in ScreenTimeHierarchy.Nodes(xml))
            {
                var labels = new[]
                {
                    node.Attribute("text")?.Value,
                    node.Attribute("content-desc")?.Value,
                };
                if (
                    !labels.Any(s =>
                        s is "Share" or "SHARE" or "Post" or "POST" or "Publish" or "Share now"
                    )
                )
                    continue;
                // Create-menu Post is navigation; only that registered menu is exempt.
                if (FacebookLayoutRegistry.Match(xml, FacebookScreenKind.CreateMenu).IsMatch)
                    continue;
                var bounds = System.Text.RegularExpressions.Regex.Matches(
                    node.Attribute("bounds")?.Value ?? "",
                    @"\d+"
                );
                if (
                    bounds.Count == 4
                    && x >= int.Parse(bounds[0].Value)
                    && x <= int.Parse(bounds[2].Value)
                    && y >= int.Parse(bounds[1].Value)
                    && y <= int.Parse(bounds[3].Value)
                )
                {
                    PublishAttempts++;
                    throw new Exception("Exact publication-labelled tap blocked by diagnostic");
                }
            }
            foreach (
                var kind in new[]
                {
                    FacebookScreenKind.ReelComposer,
                    FacebookScreenKind.PhotoComposer,
                    FacebookScreenKind.StoryEditor,
                }
            )
            {
                var publish = FacebookLayoutRegistry
                    .Match(xml, kind)
                    .Control(FacebookControlRole.Publish);
                if (publish != null && Math.Abs(publish.X - x) < 90 && Math.Abs(publish.Y - y) < 45)
                {
                    PublishAttempts++;
                    throw new Exception("Publish tap blocked by diagnostic");
                }
            }
            return await Real.TapAsync(index, x, y, Path, ct);
        }
    }
}
