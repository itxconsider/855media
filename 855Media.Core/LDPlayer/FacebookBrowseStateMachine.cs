using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

public enum FacebookBrowseState
{
    Started,
    Ready,
    AwaitingObservation,
    Recovering,
    Stopped,
}

public enum FacebookBrowseAction
{
    Scroll,
    Interact,
}

public sealed record FacebookBrowseDecision(
    bool Allowed,
    bool InteractionReady,
    FacebookBrowseState State,
    FacebookLayoutMatch Match,
    string Reason
)
{
    public string Diagnostic =>
        $"allowed={Allowed}; interactionReady={InteractionReady}; state={State}; reason={Reason}; {Match.Diagnostic}";
}

/// <summary>One destination on one device. Every input consumes its captured observation.</summary>
public sealed class FacebookBrowseStateMachine
{
    public FacebookScreenKind Screen { get; }
    public FacebookBrowseState State { get; private set; }
    private readonly HashSet<Guid> _seen = [];
    private FacebookLayoutObservation? _observation;
    private FacebookBrowseDecision? _decision;
    private string? _feedViewport;

    public FacebookBrowseStateMachine(FacebookScreenKind screen)
    {
        if (screen is not (FacebookScreenKind.NewsFeed or FacebookScreenKind.ReelsFeed))
            throw new ArgumentOutOfRangeException(nameof(screen));
        Screen = screen;
    }

    public FacebookBrowseDecision Observe(FacebookLayoutObservation observation)
    {
        var match = FacebookLayoutRegistry.Match(observation, Screen);
        if (State == FacebookBrowseState.Stopped)
            return Deny(match, "browsing-stopped");
        if (!_seen.Add(observation.Id))
            return Deny(match, "fresh-observation-required");
        _observation = observation;
        if (match.Status == FacebookLayoutStatus.Blocked)
        {
            State = FacebookBrowseState.Stopped;
            _feedViewport = null;
            return Deny(match, "blocking-screen");
        }
        if (match.IsMatch && match.Browse != null)
        {
            _feedViewport = match.Browse.FeedViewport;
            State = FacebookBrowseState.Ready;
            return _decision = new(
                true,
                match.Browse.InteractionReady,
                State,
                match,
                "destination-recognized"
            );
        }
        // A list identity supplies continuity only after independently recognizing Feed.
        // Loading, overlays, conflicting tabs, inputs, and ambiguous matches cannot use it.
        if (
            Screen == FacebookScreenKind.NewsFeed
            && _feedViewport != null
            && match.Status == FacebookLayoutStatus.Unknown
            && !observation.Screen.AllElements.Any(e =>
                e.ClassName.Contains("ProgressBar", StringComparison.OrdinalIgnoreCase)
            )
            && ScreenTimeNavigation.FeedViewportSignature(observation.Xml, scrollOnly: true)
                == _feedViewport
        )
        {
            State = FacebookBrowseState.Ready;
            return _decision = new(
                true,
                false,
                State,
                match,
                "verified-feed-continuity-scroll-only"
            );
        }
        State = FacebookBrowseState.AwaitingObservation;
        return Deny(match, "destination-not-recognized");
    }

    public bool TryBeginInput(FacebookBrowseAction action)
    {
        if (action is not (FacebookBrowseAction.Scroll or FacebookBrowseAction.Interact))
            throw new ArgumentOutOfRangeException(nameof(action));
        if (
            State != FacebookBrowseState.Ready
            || _observation == null
            || _decision?.Allowed != true
            || (action == FacebookBrowseAction.Interact && !_decision.InteractionReady)
        )
            return false;
        if (action == FacebookBrowseAction.Interact)
            _feedViewport = null;
        State = FacebookBrowseState.AwaitingObservation;
        return true;
    }

    public void BeginRecovery()
    {
        if (State == FacebookBrowseState.Stopped)
            return;
        _observation = null;
        _decision = null;
        _feedViewport = null;
        State = FacebookBrowseState.Recovering;
    }

    public void Stop()
    {
        _feedViewport = null;
        _observation = null;
        _decision = null;
        State = FacebookBrowseState.Stopped;
    }

    private FacebookBrowseDecision Deny(FacebookLayoutMatch match, string reason)
    {
        _decision = new(false, false, State, match, reason);
        return _decision;
    }
}
