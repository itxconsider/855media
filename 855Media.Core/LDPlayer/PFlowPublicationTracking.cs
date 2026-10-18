using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace _855Media.Core.LDPlayer;

// Publication observations are separate from the durable job outcome. In
// particular, a stage alone never authorizes cleanup or another Share tap;
// the completion policy must check current pending states and errors first.
public enum PFlowPublicationStage
{
    NotStarted,
    AwaitingConfirmation,
    WaitingForUpload,
    Uploading,
    Processing,
    CopyrightScanning,
    Publishing,
    UploadComplete,
    RefreshingFeed,
    RefreshingProfile,
    CheckingProfile,
    Published,
    PublicationError,
    Unconfirmed,
    CheckingStory,
    NotPublished,
}

public sealed record PFlowPublicationTransition(
    PFlowPublicationStage Stage,
    DateTime StartedUtc,
    string Signal
);

internal static class PFlowPublicationTracking
{
    internal sealed record Signal(PFlowPublicationStage Stage, string Evidence);

    private static readonly (
        PFlowPublicationStage Stage,
        string Pattern,
        string Evidence
    )[] PendingRules =
    [
        (
            PFlowPublicationStage.CopyrightScanning,
            @"^(?:Running copyright scan|Checking (?:for )?copyright)(?:\b|[. …])",
            "Facebook UI: copyright scan"
        ),
        (
            PFlowPublicationStage.Processing,
            @"^Processing(?: your (?:reel|story|post))?(?:\b|[. …])",
            "Facebook UI: processing"
        ),
        (
            PFlowPublicationStage.Publishing,
            @"^(?:Posting(?: your (?:reel|story|post))?|Sharing your (?:reel|story|post))(?:\b|[. …])",
            "Facebook UI: publishing"
        ),
        (
            PFlowPublicationStage.Uploading,
            @"^Uploading(?: your (?:reel|story|post))?(?:\b|[. …])",
            "Facebook UI: uploading"
        ),
        (
            PFlowPublicationStage.WaitingForUpload,
            @"^Waiting to upload(?:\b|[. …])",
            "Facebook UI: waiting to upload"
        ),
    ];

    internal static Signal? Pending(string? xml)
    {
        var labels = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                !n.AncestorsAndSelf()
                    .Any(a =>
                        a.Attribute("visible-to-user")?.Value == "false"
                        || (a.Attribute("class")?.Value ?? "").Contains(
                            "EditText",
                            StringComparison.Ordinal
                        )
                        || (
                            a.Attribute("package")?.Value is { Length: > 0 } package
                            && package != "com.facebook.katana"
                            && package != "com.android.systemui"
                        )
                    )
            )
            .SelectMany(n =>
                new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }
            )
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToArray();
        // A more specific active state wins when Facebook exposes overlapping labels.
        // No stage is inferred from another, and copyright scanning is optional.
        foreach (var rule in PendingRules)
            if (labels.Any(s => Regex.IsMatch(s, rule.Pattern, RegexOptions.IgnoreCase)))
                return new(rule.Stage, rule.Evidence);
        return null;
    }

    internal static bool Transition(
        PFlowJob job,
        PFlowPublicationStage stage,
        string signal,
        DateTime? utc = null
    )
    {
        if (job.PublicationStage == stage)
            return false;
        var at = utc ?? DateTime.UtcNow;
        job.PublicationStage = stage;
        job.PublicationStageStartedUtc = at;
        job.PublicationStageEvidence = signal;
        job.PublicationTransitions.Add(new(stage, at, signal));
        job.CurrentStep = Label(stage);
        job.StepStartedUtc = at;
        return true;
    }

    internal static void Reset(PFlowJob job)
    {
        job.PublicationStage = PFlowPublicationStage.NotStarted;
        job.PublicationStageStartedUtc = null;
        job.PublicationStageEvidence = "";
        job.PublicationErrorMessage = "";
        job.PublicationErrorAttribute = "";
        job.PublicationTransitions.Clear();
    }

    public static string Label(PFlowPublicationStage stage) =>
        stage switch
        {
            PFlowPublicationStage.WaitingForUpload => "Waiting for Facebook upload",
            PFlowPublicationStage.Uploading => "Facebook uploading",
            PFlowPublicationStage.Processing => "Facebook processing",
            PFlowPublicationStage.CopyrightScanning => "Facebook copyright scan",
            PFlowPublicationStage.Publishing => "Facebook publishing",
            PFlowPublicationStage.UploadComplete =>
                "Upload complete; awaiting publication confirmation",
            PFlowPublicationStage.AwaitingConfirmation =>
                "Waiting for Facebook publication confirmation",
            PFlowPublicationStage.RefreshingFeed =>
                "Refreshing Home feed after upload / processing",
            PFlowPublicationStage.RefreshingProfile => "Refreshing target profile",
            PFlowPublicationStage.CheckingStory => "Checking target Story publication",
            PFlowPublicationStage.CheckingProfile =>
                "Checking target profile caption and publication time",
            PFlowPublicationStage.Published => "Publication confirmed",
            PFlowPublicationStage.PublicationError => "Facebook reported a publication error",
            PFlowPublicationStage.Unconfirmed => "Publication unconfirmed; review required",
            PFlowPublicationStage.NotPublished => "Manually verified not published",
            _ => "Publication not started",
        };
}
