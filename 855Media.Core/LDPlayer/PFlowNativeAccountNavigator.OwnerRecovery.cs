using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowNativeAccountNavigator
{
    internal static bool OwnerRecoveryFrame(string xml, string package) =>
        ScreenTimeHierarchy.Nodes(xml).Length == 0
        || !PFlowUiPrivacy.ContainsInput(xml)
            && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            && !PFlowShareDestination.IsChooser(xml)
            && !PFlowConfirmation.HasPendingPublication(xml)
            && ScreenTimeHierarchy
                .Nodes(xml)
                .All(n =>
                    n.Attribute("package")?.Value is not { Length: > 0 } p
                    || p == package
                    || p == "com.android.systemui"
                )
            && (
                PFlowPostingNameVerification.SafeNameResponseContext(xml, package)
                || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
            );

    private static bool BlockedOwnerMenu(string xml, string owner) =>
        PFlowPostingNameVerification.SafeNameResponseContext(xml, "com.facebook.katana")
        && MenuSurface(xml)
        && PFlowPostingNameVerification.PersonalShortcut(xml, owner, requireUsable: false) != null
        && PFlowPostingNameVerification.PersonalShortcut(xml, owner) == null;

    private async Task<bool> RecoverBlockedOwnerMenuAsync(
        PFlowAccountBinding binding,
        string owner,
        CancellationToken ct
    ) => await RecoverOwnerSwitcherAsync(binding, owner, ct, fromFeed: false);

    private static bool RecoverableOwnerFeed(string xml, string package) =>
        OwnerRecoveryFrame(xml, package)
        && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch;

    private async Task<bool> RecoverOwnerSwitcherAsync(
        PFlowAccountBinding binding,
        string owner,
        CancellationToken ct,
        bool fromFeed
    )
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(fromFeed ? 3 : 2));
        string? process = null;
        async Task RequireFresh(CancellationToken token)
        {
            await _preparation.RequireWorkerAsync(binding, token);
            if (requireRecoveryIdle != null)
                await requireRecoveryIdle(binding, token);
            var xml = await Screen(binding, token);
            var pid = (
                await ld.ExecuteAdbAsync(
                    binding.DeviceIndex,
                    "shell pidof com.facebook.katana",
                    path,
                    token
                )
            ).Trim();
            if (
                pid.Length == 0
                || !pid.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .All(p => p.All(char.IsAsciiDigit))
                || process != null && pid != process
                || !OwnerRecoveryFrame(xml, binding.AppPackage)
            )
                throw new InvalidOperationException(
                    "Facebook changed while returning to the saved account."
                );
            process ??= pid;
        }
        try
        {
            await RequireFresh(budget.Token);
            var initial = await Screen(binding, budget.Token);
            if (
                fromFeed
                    ? !RecoverableOwnerFeed(initial, binding.AppPackage)
                    : !BlockedOwnerMenu(initial, owner)
            )
                return false;
            await PFlowTrace.SessionAsync(
                binding.DeviceIndex,
                "account.owner.shared-switcher",
                fromFeed
                    ? "Recognized Feed; verifying saved owner through complete native actor list; no API or restart."
                    : "Owner shortcut is not usable; using complete native actor list; no shortcut tap or API.",
                budget.Token
            );
            var service = new PFlowPersonaSwitcherService(
                ld,
                null,
                _delay,
                () => DateTime.UtcNow,
                binding,
                savedTargets?.Invoke(binding)
            );
            return await service.RecoverPersonalOwnerAsync(
                binding.DeviceIndex,
                owner,
                path,
                budget.Token,
                RequireFresh
            );
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (fromFeed)
                throw new InvalidOperationException(
                    "Saved personal-account recovery timed out before final identity confirmation; Facebook was left open."
                );
            return false;
        }
    }
}
