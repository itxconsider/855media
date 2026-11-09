using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static partial class PFlowPostingNameVerification
{
    public static bool PreviousUploadPending(string? xml) =>
        PFlowConfirmation.HasPendingPublication(xml);

    internal static readonly TimeSpan HeaderResponseWait = TimeSpan.FromSeconds(60);

    private sealed class HeaderResponseUnavailable()
        : InvalidOperationException(
            "Facebook's active profile header did not finish loading. No further action was authorized."
        );

    public static Task<PFlowNativeIdentityScope> ActivateAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        FacebookPageTarget target,
        IEnumerable<FacebookPageTarget> savedTargets,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireRecoveryIdle = null,
        Func<string, Task>? progress = null,
        Func<string, CancellationToken, Task<string?>>? confirmedBackgroundObservation = null
    ) =>
        ActivateWithResponseWaitAsync(
            ld,
            binding,
            target,
            savedTargets,
            path,
            ct,
            requireRecoveryIdle,
            Task.Delay,
            () => DateTimeOffset.UtcNow,
            progress,
            confirmedBackgroundObservation
        );

    private static bool SafeResponseFrame(string xml, string package) =>
        !PFlowConfirmation.HasPendingPublication(xml) && SafeNameResponseContext(xml, package);

    internal static bool SafeNameResponseContext(string xml, string package)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        if (nodes.Length == 0)
            return true; // Observation may be empty; foreground is checked separately.
        return !PFlowUiPrivacy.ContainsInput(xml)
            && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            && !PFlowShareDestination.IsChooser(xml)
            && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            && nodes.All(n =>
                string.IsNullOrEmpty(n.Attribute("package")?.Value)
                || n.Attribute("package")?.Value == package
                || n.Attribute("package")?.Value == "com.android.systemui"
            )
            && (
                nodes.Length == 0
                || !nodes.Any(n => n.Attribute("package")?.Value == package)
                || PFlowPostTargetSession.CanContinueContext(xml, package)
                || LoadingResponse(xml)
                || PFlowIdentitySwitchLayout.IsSwitching(xml)
            );
    }

    internal static bool LoadingResponse(string xml)
    {
        var labels = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                n.Attribute("package")?.Value == "com.facebook.katana"
                && !n.AncestorsAndSelf().Any(a => a.Attribute("visible-to-user")?.Value == "false")
            )
            .SelectMany(n =>
                new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }
            )
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToArray();
        return labels.All(s =>
                s.TrimEnd('.', '\u2026').Equals("Loading", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Loading...", StringComparison.OrdinalIgnoreCase)
                || s.TrimEnd('.', '\u2026')
                    .Equals("Please wait", StringComparison.OrdinalIgnoreCase)
            )
            || labels.Any(s => s.Equals("Close menu.", StringComparison.OrdinalIgnoreCase))
                && !labels.Any(s =>
                    s.EndsWith(", see your profile", StringComparison.OrdinalIgnoreCase)
                )
                && ActiveHeaderName(xml) == null
            || PFlowIdentitySwitchLayout.IsSwitching(xml);
    }

    internal static async Task WaitForSelectedSwitcherCloseAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        string? path,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTimeOffset> now
    )
    {
        var deadline = now().AddSeconds(30);
        string? process = null;
        for (var poll = 0; poll <= 30; poll++)
        {
            ct.ThrowIfCancellationRequested();
            var xml = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
            var activity =
                await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
            if (
                activity != binding.AppPackage
                && !activity.StartsWith(binding.AppPackage + "/", StringComparison.Ordinal)
            )
                throw new InvalidOperationException(
                    "Facebook lost foreground while its switcher was closing."
                );
            if (!PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml))
            {
                if (
                    process != null
                    && (
                        await ld.ExecuteAdbAsync(
                            binding.DeviceIndex,
                            "shell pidof com.facebook.katana",
                            path,
                            ct
                        )
                    ).Trim() != process
                )
                    throw new InvalidOperationException(
                        "Facebook process changed while its switcher was closing."
                    );
                return;
            }
            if (
                PFlowUiPrivacy.ContainsInput(xml)
                || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                || PFlowShareDestination.IsChooser(xml)
                || PFlowConfirmation.HasPendingPublication(xml)
                || ScreenTimeHierarchy
                    .Nodes(xml)
                    .Any(n =>
                        n.Attribute("package")?.Value is { Length: > 0 } p
                        && p != binding.AppPackage
                        && p != "com.android.systemui"
                    )
            )
                throw new InvalidOperationException(
                    "Unsafe screen appeared while Facebook's switcher was closing."
                );
            var current = (
                await ld.ExecuteAdbAsync(
                    binding.DeviceIndex,
                    "shell pidof com.facebook.katana",
                    path,
                    ct
                )
            ).Trim();
            process ??= current;
            if (
                current.Length == 0
                || !current.Split(' ').All(p => p.Length > 0 && p.All(char.IsAsciiDigit))
                || current != process
            )
                throw new InvalidOperationException(
                    "Facebook process changed while its switcher was closing."
                );
            if (poll == 30 || now() >= deadline)
                throw new InvalidOperationException(
                    "Facebook's profile switcher did not close after selection. No repeated selection or restart was attempted."
                );
            if (poll == 0)
                await PFlowTrace.SessionAsync(
                    binding.DeviceIndex,
                    "identity.switcher.close-wait",
                    "timeoutSeconds=30; noTap=true; noRestart=true",
                    ct
                );
            await delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    internal static async Task<PFlowNativeIdentityScope> ActivateWithResponseWaitAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        FacebookPageTarget target,
        IEnumerable<FacebookPageTarget> savedTargets,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireRecoveryIdle,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTimeOffset> now,
        Func<string, Task>? progress = null,
        Func<string, CancellationToken, Task<string?>>? confirmedBackgroundObservation = null
    )
    {
        var saved = savedTargets.Select(t => t.CreateSnapshot()).ToArray();
        target = target.CreateSnapshot();
        if (
            binding.AppPackage != "com.facebook.katana"
            || target.AccountBinding != binding
            || target.UserUid != binding.OwnerUid
            || !MatchesSavedTarget(target, saved)
        )
            throw new InvalidOperationException(
                "Saved target name is missing, ambiguous or belongs to another account."
            );

        DateTimeOffset? uploadDeadline = null;
        async Task<string> ReadSafe()
        {
            ct.ThrowIfCancellationRequested();
            var xml = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
            var activity =
                await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
            if (confirmedBackgroundObservation != null)
                xml = await confirmedBackgroundObservation(xml, ct) ?? xml;
            if (
                (
                    activity != binding.AppPackage
                    && !activity.StartsWith(binding.AppPackage + "/", StringComparison.Ordinal)
                ) || !SafeNameResponseContext(xml, binding.AppPackage)
            )
            {
                await PFlowTrace.SessionAsync(
                    binding.DeviceIndex,
                    "identity.name.context-rejected",
                    $"foregroundOwned={activity == binding.AppPackage || activity.StartsWith(binding.AppPackage + "/", StringComparison.Ordinal)}; input={PFlowUiPrivacy.ContainsInput(xml)}; authentication={PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))}; chooser={PFlowShareDestination.IsChooser(xml)}; switcher={PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)}; loading={LoadingResponse(xml)}; pending={PFlowConfirmation.HasPendingPublication(xml)}",
                    ct
                );
                throw new InvalidOperationException(
                    "Facebook reached an unsafe or foreign screen while waiting for its name. No restart attempted."
                );
            }
            return xml;
        }

        async Task<string> Observe()
        {
            var xml = await ReadSafe();
            if (!PFlowConfirmation.HasPendingPublication(xml))
                return xml;
            uploadDeadline ??= now().AddSeconds(30);
            var message = "Waiting for Facebook to finish the previous upload (up to 30s).";
            if (progress != null)
                await progress(message);
            await PFlowTrace.SessionAsync(
                binding.DeviceIndex,
                "identity.previous-upload.wait",
                "pendingUi=true; timeoutSeconds=30; noNavigation=true; noRestart=true; submitted=false",
                ct
            );
            var process = (
                await ld.ExecuteAdbAsync(
                    binding.DeviceIndex,
                    "shell pidof com.facebook.katana",
                    path,
                    ct
                )
            ).Trim();
            var clear = 0;
            for (var poll = 0; poll < 32 && now() < uploadDeadline.Value; poll++)
            {
                ct.ThrowIfCancellationRequested();
                var currentProcess = (
                    await ld.ExecuteAdbAsync(
                        binding.DeviceIndex,
                        "shell pidof com.facebook.katana",
                        path,
                        ct
                    )
                ).Trim();
                if (
                    process.Length == 0
                    || !process.Split(' ').All(p => p.All(char.IsAsciiDigit))
                    || currentProcess != process
                )
                    throw new InvalidOperationException(
                        "Facebook process changed while waiting for the previous upload. No restart or new submission attempted."
                    );
                if (
                    !PFlowConfirmation.HasPendingPublication(xml)
                    && ScreenTimeHierarchy
                        .Nodes(xml)
                        .Any(n => n.Attribute("package")?.Value == binding.AppPackage)
                )
                {
                    var notifications = await ld.ExecuteAdbAsync(
                        binding.DeviceIndex,
                        "shell dumpsys notification --noredact",
                        path,
                        ct
                    );
                    if (
                        PFlowConfirmation.HasActiveNotificationList(notifications)
                        && !PFlowConfirmation
                            .ReadUploadNotifications(notifications, binding.AppPackage)
                            .Any(n => n.Ongoing)
                    )
                        clear++;
                    else
                        clear = 0;
                    if (clear >= 2)
                    {
                        if (requireRecoveryIdle != null)
                            await requireRecoveryIdle(ct);
                        xml = await ReadSafe();
                        if (
                            !PFlowConfirmation.HasPendingPublication(xml)
                            && (
                                await ld.ExecuteAdbAsync(
                                    binding.DeviceIndex,
                                    "shell pidof com.facebook.katana",
                                    path,
                                    ct
                                )
                            ).Trim() == process
                        )
                        {
                            if (progress != null)
                                await progress("Previous upload cleared; verifying target name.");
                            await PFlowTrace.SessionAsync(
                                binding.DeviceIndex,
                                "identity.previous-upload.cleared",
                                "freshUi=true; inactiveUploadNotifications=true; Graph=0; noRestart=true",
                                ct
                            );
                            return xml;
                        }
                        clear = 0;
                    }
                }
                else
                    clear = 0;
                await delay(TimeSpan.FromSeconds(1), ct);
                xml = await ReadSafe();
            }
            throw new InvalidOperationException(
                "Facebook is still finishing the previous upload, or upload status is unavailable. No restart or new post submission attempted; wait and retry this queued item."
            );
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await ActivateAttempt(attempt);
            }
            catch (HeaderResponseUnavailable) when (attempt == 0 && requireRecoveryIdle != null)
            {
                // Caller owns the LD lease and checks durable pending/review work.
                await requireRecoveryIdle(ct);
                ct.ThrowIfCancellationRequested();
                var xml = await Observe();
                var lateName = ActiveHeaderName(xml);
                if (PFlowNameMatch.Same(lateName ?? "", target.PageName))
                    continue; // Late response: observe afresh without restarting.
                if (lateName != null || !LoadingResponse(xml))
                    throw new InvalidOperationException(
                        "Facebook's header changed before loading recovery; restart stopped."
                    );
                var dump = await ld.ExecuteAdbAsync(
                    binding.DeviceIndex,
                    "shell dumpsys notification --noredact",
                    path,
                    ct
                );
                if (
                    !PFlowConfirmation.HasActiveNotificationList(dump)
                    || PFlowConfirmation
                        .ReadUploadNotifications(dump, binding.AppPackage)
                        .Any(n => n.Ongoing)
                )
                    throw new InvalidOperationException(
                        "Upload state is active or unavailable; loading restart stopped."
                    );
                await PFlowTrace.SessionAsync(
                    binding.DeviceIndex,
                    "identity.name.restart",
                    "attempt=1; reason=header-loading-timeout; appDataPreserved=true; Graph calls=0",
                    ct
                );
                if (
                    !await ld.StopFacebookAppAsync(binding.DeviceIndex, path, ct)
                    || !await ld.LaunchAppAsync(binding.DeviceIndex, binding.AppPackage, path, ct)
                )
                    throw new InvalidOperationException(
                        "Facebook could not be reopened for the one loading retry."
                    );
                var foregroundReady = false;
                for (var poll = 0; poll < 20; poll++)
                {
                    ct.ThrowIfCancellationRequested();
                    var activity =
                        await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct)
                        ?? "";
                    if (
                        activity == binding.AppPackage
                        || activity.StartsWith(binding.AppPackage + "/", StringComparison.Ordinal)
                    )
                    {
                        foregroundReady = true;
                        break;
                    }
                    if (
                        !string.IsNullOrEmpty(activity)
                        && !activity.StartsWith("com.ldmnq.launcher3/", StringComparison.Ordinal)
                        && !activity.StartsWith("com.android.launcher3/", StringComparison.Ordinal)
                    )
                        throw new InvalidOperationException(
                            "Another app opened during Facebook loading recovery."
                        );
                    await delay(TimeSpan.FromSeconds(1), ct);
                }
                if (!foregroundReady)
                    throw new InvalidOperationException(
                        "Facebook did not return to foreground after its loading retry."
                    );
                await delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        throw new HeaderResponseUnavailable();

        async Task<PFlowNativeIdentityScope> ActivateAttempt(int attempt)
        {
            var deadline = now() + HeaderResponseWait;
            var xml = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
            if (PFlowConfirmation.HasPendingPublication(xml))
                xml = await Observe();
            var waitLogged = false;
            async Task Wait()
            {
                if (!waitLogged)
                {
                    waitLogged = true;
                    await PFlowTrace.SessionAsync(
                        binding.DeviceIndex,
                        "identity.name.loading-wait",
                        $"attempt={attempt + 1}; timeoutSeconds={HeaderResponseWait.TotalSeconds}; Graph calls=0",
                        ct
                    );
                }
                await delay(TimeSpan.FromSeconds(1), ct);
            }
            // A blank window or incomplete Menu is not a failed selection. Let it render
            // before trying any navigation. Existing switcher sheets are handled natively.
            while (
                LoadingResponse(xml) && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            )
            {
                xml = await Observe();
                if (!LoadingResponse(xml))
                    break;
                if (now() >= deadline)
                    throw new HeaderResponseUnavailable();
                await Wait();
            }
            var current = ActiveHeaderName(xml);
            var selectionSucceeded = true;
            if (!PFlowNameMatch.Same(current ?? "", target.PageName))
            {
                selectionSucceeded = await new PFlowPersonaSwitcherService(
                    ld,
                    binding,
                    saved
                ).SelectNativeTargetAsync(
                    binding.DeviceIndex,
                    target,
                    path,
                    ct,
                    postingNameOnly: true,
                    restartOnNavigationFailure: false
                );
                await WaitForSelectedSwitcherCloseAsync(ld, binding, path, ct, delay, now);
            }
            if (!selectionSucceeded)
            {
                // The guarded outer loop owns the single restart budget, including
                // a switch that timed out on a loader rather than a readable wrong actor.
                xml = await Observe();
                while (LoadingResponse(xml))
                {
                    if (now() >= deadline)
                        throw new HeaderResponseUnavailable();
                    await Wait();
                    xml = await Observe();
                }
                if (!PFlowNameMatch.Same(ActiveHeaderName(xml) ?? "", target.PageName))
                    throw new InvalidOperationException(
                        "Target could not be selected in Facebook's switcher."
                    );
            }

            string? observed = null;
            var otherNameSeen = false;
            var menuAttempts = 0;
            var feedRecoveryAttempted = false;
            while (now() < deadline)
            {
                xml = await Observe();
                if (
                    !feedRecoveryAttempted
                    && ActiveHeaderName(xml) == null
                    && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
                )
                {
                    feedRecoveryAttempted = true;
                    xml = await RecoverSelectedNameFeedAsync(
                        ld,
                        binding,
                        path,
                        ct,
                        requireRecoveryIdle,
                        Observe,
                        delay,
                        now,
                        deadline
                    );
                }
                observed = ActiveHeaderName(xml);
                if (PFlowNameMatch.Same(observed ?? "", target.PageName))
                {
                    var process = (
                        await ld.ExecuteAdbAsync(
                            binding.DeviceIndex,
                            "shell pidof com.facebook.katana",
                            path,
                            ct
                        ) ?? ""
                    ).Trim();
                    var proof = PFlowNativeIdentityScope.FromSelectedName(
                        binding,
                        process,
                        target,
                        new(observed!, target.Type),
                        saved
                    );
                    if (proof == null)
                        throw new InvalidOperationException(
                            "Saved target name/type or account binding changed during activation."
                        );
                    if (await proof.SessionUnchangedAsync(ld, path, ct))
                        return proof;
                }
                else if (observed != null)
                    otherNameSeen = true;
                if (
                    observed == null
                    && menuAttempts < 2
                    && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
                )
                {
                    menuAttempts++;
                    await OpenNativeMenuAsync(ld, binding.DeviceIndex, path, ct);
                }
                await Wait();
            }
            if (observed == null && !otherNameSeen && LoadingResponse(xml))
                throw new HeaderResponseUnavailable();
            throw new InvalidOperationException(
                observed == null
                    ? "Facebook's active profile header could not be read after selection. No further action was authorized."
                : PFlowNameMatch.Same(observed, target.PageName)
                    ? "Facebook's actor process/foreground did not stabilize. No further action was authorized."
                : "Facebook switched to a different complete profile name than the saved target. No further action was authorized."
            );
        }
    }
}
