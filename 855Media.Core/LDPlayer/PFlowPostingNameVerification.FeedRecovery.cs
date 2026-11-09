using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static partial class PFlowPostingNameVerification
{
    // Navigation only: the caller still proves the exact actor name and session.
    internal static async Task<string> RecoverSelectedNameFeedAsync(
        ILDPlayerService ld,
        PFlowAccountBinding binding,
        string? path,
        CancellationToken ct,
        Func<CancellationToken, Task>? requireIdle,
        Func<Task<string>> observe,
        Func<TimeSpan, CancellationToken, Task> delay,
        Func<DateTimeOffset> now,
        DateTimeOffset deadline
    )
    {
        var process = PFlowProcessIdentity.Normalize(
            await ld.ExecuteAdbAsync(
                binding.DeviceIndex,
                "shell pidof com.facebook.katana",
                path,
                ct
            )
        );
        if (process == null)
            throw new InvalidOperationException(
                "Facebook's process could not be verified before profile recovery."
            );
        var reveals = 0;
        var reopened = false;
        var attempted = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        var navigationAttempts = 0;
        var settleFrames = 0;
        var settleUntil = DateTimeOffset.MinValue;
        for (var poll = 0; poll < 30 && now() < deadline; poll++)
        {
            ct.ThrowIfCancellationRequested();
            if (requireIdle != null)
                await requireIdle(ct);
            var xml = await observe();
            var activity =
                await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
            var currentProcess = await ld.ExecuteAdbAsync(
                binding.DeviceIndex,
                "shell pidof com.facebook.katana",
                path,
                ct
            );
            if (
                !PFlowProcessIdentity.Same(process, currentProcess)
                || activity != binding.AppPackage
                    && !activity.StartsWith(binding.AppPackage + "/", StringComparison.Ordinal)
                || !SafeResponseFrame(xml, binding.AppPackage)
            )
                throw new InvalidOperationException(
                    "Facebook changed during profile recovery. No restart or new submission was attempted."
                );
            if (now() >= deadline)
                break;
            if (ActiveHeaderName(xml) != null)
                return xml;
            if (ScreenTimeHierarchy.Nodes(xml).Length == 0 || LoadingResponse(xml))
            {
                await delay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            if (!FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch)
                return xml; // Never navigate from an unknown/profile/settings surface here.
            if (settleFrames > 0 || now() < settleUntil)
            {
                settleFrames = Math.Max(0, settleFrames - 1);
                await delay(TimeSpan.FromMilliseconds(500), ct);
                continue;
            }
            var screen = AtxAgentClient.ParseBloksHierarchy(xml);
            var route = PFlowPersonaSwitcherService.PlanNativeRoute(
                screen,
                e => !attempted.Contains(e.ContentDesc + "\n" + e.Text)
            );
            if (navigationAttempts >= 3)
                break;
            if (route.Navigation is { } navigation)
            {
                var matches = screen
                    .AllElements.Where(e =>
                        PFlowPersonaSwitcherService.IsNativeNavigationTarget(e, screen)
                        && e.ContentDesc == navigation.ContentDesc
                    )
                    .Select(e => (e.CenterX, e.CenterY))
                    .Distinct()
                    .Count();
                if (matches != 1)
                    throw new InvalidOperationException(
                        "Profile navigation is ambiguous; no control was tapped."
                    );
                attempted.Add(navigation.ContentDesc + "\n" + navigation.Text);
                navigationAttempts++;
                if (
                    !await ld.TapAsync(
                        binding.DeviceIndex,
                        navigation.CenterX,
                        navigation.CenterY,
                        path,
                        ct
                    )
                )
                    throw new InvalidOperationException(
                        "Could not open the selected Page's profile or Menu."
                    );
                await PFlowTrace.SessionAsync(
                    binding.DeviceIndex,
                    "identity.header.navigation",
                    $"attempt={navigationAttempts}; maximum=3; freshSemanticControl=true; identityVerified=false; noRestart=true",
                    ct
                );
                settleFrames = 2;
                settleUntil = now().AddSeconds(2);
            }
            else if (
                route.Kind == PFlowPersonaSwitcherService.NativeRouteKind.Reveal
                && reveals < 3
            )
            {
                var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                if (
                    width <= 0
                    || height <= 0
                    || !await ld.SwipeAsync(
                        binding.DeviceIndex,
                        width / 2,
                        height / 3,
                        width / 2,
                        height * 3 / 4,
                        350,
                        path,
                        ct
                    )
                )
                    throw new InvalidOperationException("Could not reveal Facebook's navigation.");
                reveals++;
                await PFlowTrace.SessionAsync(
                    binding.DeviceIndex,
                    "identity.header.reveal",
                    $"attempt={reveals}; maximum=3",
                    ct
                );
            }
            else if (
                !reopened
                && reveals >= 3
                && route.Kind == PFlowPersonaSwitcherService.NativeRouteKind.Reveal
            )
            {
                reopened = true;
                if (
                    !await ld.OpenDeepLinkAsync(
                        binding.DeviceIndex,
                        "fb://feed",
                        binding.AppPackage,
                        path,
                        ct
                    )
                )
                    throw new InvalidOperationException(
                        "Could not reopen Feed for profile verification."
                    );
                attempted.Clear(); // A single fresh Feed permits one bounded retry of an inert control.
                settleFrames = 2;
                settleUntil = now().AddSeconds(2);
                await PFlowTrace.SessionAsync(
                    binding.DeviceIndex,
                    "identity.header.feed-recovery",
                    "revealsExhausted=3; maximumReopens=1; identityVerified=false; noRestart=true",
                    ct
                );
            }
            await delay(TimeSpan.FromMilliseconds(750), ct);
        }
        throw new InvalidOperationException(
            "Page selected, but its name could not be verified before the recovery timeout. No further action was authorized."
        );
    }
}
