using System;
using System.Collections.Generic;
using System.Linq;

namespace _855Media.Core.LDPlayer;

// An observed work cycle followed by a stable, clear Facebook surface is an
// accepted completion route. A success banner and a copyright stage are optional.
internal static class PFlowPublicationCycle
{
    internal const string EvidencePrefix = "Facebook publication cycle completed:";

    internal static string? ReadCompletedMediaStages(
        PFlowJob job,
        string xml,
        bool ongoingUpload,
        ISet<string>? before = null
    )
    {
        if (
            job.PostType is not ("Reel" or "Video" or "Picture")
            || job.State != PFlowState.Submitted
            || !job.SubmittedUtc.HasValue
            || ongoingUpload
            || !PFlowProfilePublicationVerifier.Safe(xml)
            || !xml.Contains(job.AppPackage, StringComparison.Ordinal)
            || PFlowConfirmation.HasPendingPublication(xml)
            || PFlowPublicationFailure.Read(xml, job.Caption, before) != null
            || PFlowAutoPostService.IsComposer(xml, job.PostType)
            || (
                job.PostType != "Picture"
                && PFlowAutoPostService.HasComposerLayoutEvidence(xml, job.PostType)
            )
        )
            return null;
        var completed = job.PublicationTransitions.Any(t =>
            t.StartedUtc >= job.SubmittedUtc.Value
            && (
                t.Stage
                    is PFlowPublicationStage.UploadComplete
                        or PFlowPublicationStage.CopyrightScanning
                || job.PostType == "Picture" && t.Stage == PFlowPublicationStage.Publishing
            )
        );
        if (!completed)
            return null;
        return EvidencePrefix
            + " fresh upload completion or observed copyright scan / picture posting cleared; no pending publication state, Facebook error, or active upload. Home refresh and success banner optional.";
    }

    private static bool IsWork(PFlowPublicationStage stage) =>
        stage
            is PFlowPublicationStage.Uploading
                or PFlowPublicationStage.Processing
                or PFlowPublicationStage.CopyrightScanning
                or PFlowPublicationStage.Publishing;

    internal static bool WorkObserved(PFlowJob job) =>
        job.SubmittedUtc.HasValue
        && job.PublicationTransitions.Any(t =>
            IsWork(t.Stage) && t.StartedUtc >= job.SubmittedUtc.Value
        );

    private static bool SupportedSurface(PFlowJob job, string xml) =>
        FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch
        || PFlowPostTargetSession.HasOwnProfile(job, xml)
        || (
            job.PostType == "Story"
            && FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnStoryViewer).IsMatch
        );

    internal static bool Observe(
        PFlowJob job,
        PFlowPublicationSettleGate gate,
        string xml,
        TimeSpan elapsed
    )
    {
        if (WorkObserved(job) && !SupportedSurface(job, xml))
        {
            gate.Reset();
            return false;
        }
        return gate.Observe(xml, elapsed);
    }

    internal static string? Read(
        PFlowJob job,
        string xml,
        bool settled,
        bool ongoingUpload,
        ISet<string>? before = null
    )
    {
        if (
            job.State != PFlowState.Submitted
            || !WorkObserved(job)
            || !settled
            || ongoingUpload
            || !PFlowProfilePublicationVerifier.Safe(xml)
            || PFlowConfirmation.HasPendingPublication(xml)
            || PFlowPublicationFailure.Read(xml, job.Caption, before) != null
            || PFlowAutoPostService.IsComposer(xml, job.PostType)
            || (
                job.PostType != "Picture"
                && PFlowAutoPostService.HasComposerLayoutEvidence(xml, job.PostType)
            )
            || (job.PostType == "Story" && PFlowStoryLayout.HasPendingUpload(xml))
        )
            return null;
        if (!SupportedSurface(job, xml))
            return null;
        var stages = job
            .PublicationTransitions.Where(t =>
                IsWork(t.Stage) && t.StartedUtc >= job.SubmittedUtc!.Value
            )
            .Select(t => t.Stage)
            .Distinct();
        return $"{EvidencePrefix} observed {string.Join(", ", stages)}; pending indicators cleared for 30 seconds on a recognized Facebook surface, with no observed publication error or active upload. Success banner optional.";
    }
}
