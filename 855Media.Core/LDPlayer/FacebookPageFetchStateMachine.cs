using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

public enum FacebookPageFetchState
{
    Started,
    OwnerUidVerified,
    PageSelected,
    OwnProfileConfirmed,
    DashboardRequested,
    DashboardReady,
    AnalyticsRequested,
    AnalyticsReady,
    Completed,
    Stopped,
}

public enum FacebookPageFetchAction
{
    ConfirmOwnProfile,
    OpenDashboard,
    ReadDashboard,
    OpenAnalytics,
    ReadAnalytics,
}

public sealed record FacebookFetchDecision(
    bool Allowed,
    FacebookPageFetchState State,
    IReadOnlyList<string> MissingEvidence
)
{
    public string Diagnostic =>
        $"allowed={Allowed}; state={State}; missing=[{string.Join(",", MissingEvidence)}]";
}

/// <summary>
/// Per-fetch, per-target state. Layout matches cannot supply UID proof or selection acknowledgement.
/// The existing live account verifier and native switcher supply those two facts explicitly.
/// </summary>
public sealed class FacebookPageFetchStateMachine(string expectedOwnerUid, string expectedPageName)
{
    public FacebookPageFetchState State { get; private set; }
    private Guid? _lastNavigationObservation;

    private static string Normalize(string value) => Regex.Replace(value.Trim(), @"\s+", " ");

    public FacebookFetchDecision VerifyOwnerUid(string observedUid)
    {
        if (State != FacebookPageFetchState.Started)
            return Denied("owner-verification-order");
        if (
            string.IsNullOrWhiteSpace(expectedOwnerUid)
            || !expectedOwnerUid.All(char.IsAsciiDigit)
            || observedUid != expectedOwnerUid
        )
            return Denied("live-owner-uid-mismatch");
        State = FacebookPageFetchState.OwnerUidVerified;
        return Allowed();
    }

    public FacebookFetchDecision ConfirmPageSelection(bool acknowledged)
    {
        if (State != FacebookPageFetchState.OwnerUidVerified)
            return Denied("owner-uid-not-verified");
        if (!acknowledged)
            return Denied("page-switch-not-confirmed");
        State = FacebookPageFetchState.PageSelected;
        return Allowed();
    }

    public FacebookFetchDecision Advance(FacebookPageFetchAction action, FacebookLayoutMatch match)
    {
        if (State is FacebookPageFetchState.Completed or FacebookPageFetchState.Stopped)
            return Denied("fetch-ended");
        if (match.Status == FacebookLayoutStatus.Blocked)
        {
            State = FacebookPageFetchState.Stopped;
            return Denied("blocking-screen");
        }
        if (!match.IsMatch)
            return Denied("layout-not-recognized");
        var (from, screen, control, next) = action switch
        {
            FacebookPageFetchAction.ConfirmOwnProfile => (
                new[]
                {
                    FacebookPageFetchState.PageSelected,
                    FacebookPageFetchState.OwnProfileConfirmed,
                },
                FacebookScreenKind.OwnProfile,
                (FacebookControlRole?)null,
                FacebookPageFetchState.OwnProfileConfirmed
            ),
            FacebookPageFetchAction.OpenDashboard => (
                new[] { FacebookPageFetchState.OwnProfileConfirmed },
                FacebookScreenKind.OwnProfile,
                (FacebookControlRole?)FacebookControlRole.Dashboard,
                FacebookPageFetchState.DashboardRequested
            ),
            FacebookPageFetchAction.ReadDashboard => (
                new[]
                {
                    FacebookPageFetchState.DashboardRequested,
                    FacebookPageFetchState.DashboardReady,
                },
                FacebookScreenKind.Dashboard,
                (FacebookControlRole?)null,
                FacebookPageFetchState.DashboardReady
            ),
            FacebookPageFetchAction.OpenAnalytics => (
                new[] { FacebookPageFetchState.DashboardReady },
                FacebookScreenKind.Dashboard,
                (FacebookControlRole?)FacebookControlRole.Analytics,
                FacebookPageFetchState.AnalyticsRequested
            ),
            FacebookPageFetchAction.ReadAnalytics => (
                new[]
                {
                    FacebookPageFetchState.AnalyticsRequested,
                    FacebookPageFetchState.AnalyticsReady,
                },
                FacebookScreenKind.Analytics,
                (FacebookControlRole?)null,
                FacebookPageFetchState.AnalyticsReady
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        if (!from.Contains(State))
            return Denied("transition-order");
        if (match.Screen != screen)
            return Denied("unexpected-screen");
        if (
            screen == FacebookScreenKind.OwnProfile
            && (
                string.IsNullOrWhiteSpace(expectedPageName)
                || !string.Equals(
                    Normalize(match.Context.TargetName ?? ""),
                    Normalize(expectedPageName),
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
            return Denied("expected-page-context");
        if (_lastNavigationObservation == match.ObservationId)
            return Denied("fresh-observation-required");
        if (control.HasValue && match.Control(control.Value) == null)
            return Denied("control-" + control + "-unavailable-or-ambiguous");
        if (control.HasValue)
            _lastNavigationObservation = match.ObservationId;
        State = next;
        return Allowed();
    }

    public void Stop() => State = FacebookPageFetchState.Stopped;

    public void Complete()
    {
        if (
            State < FacebookPageFetchState.OwnProfileConfirmed
            || State == FacebookPageFetchState.Stopped
        )
            throw new InvalidOperationException(
                "A confirmed Page is required before completing a fetch."
            );
        State = FacebookPageFetchState.Completed;
    }

    private FacebookFetchDecision Allowed() => new(true, State, Array.Empty<string>());

    private FacebookFetchDecision Denied(string reason) => new(false, State, new[] { reason });
}
