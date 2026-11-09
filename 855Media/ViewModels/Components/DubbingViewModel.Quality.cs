using System;
using System.Collections.ObjectModel;
using System.Linq;
using _855Media.Core.Dubbing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class DubbingViewModel
{
    [ObservableProperty]
    private bool _requireTranslatedDialogue = true;

    [ObservableProperty]
    private string _translationBrief = string.Empty;

    partial void OnTranslationBriefChanged(string value) => IsProjectDirty = true;

    [ObservableProperty]
    private string _qualitySummary =
        "Run QC after editing or generating voices. No dialogue is changed by this check.";

    [ObservableProperty]
    private bool _qualityReportNeedsRefresh = true;

    public ObservableCollection<DubbingQualityIssue> QualityIssues { get; } = [];

    partial void OnRequireTranslatedDialogueChanged(bool value) => IsProjectDirty = true;

    private bool CheckStudioSynthesisReady()
    {
        try
        {
            DubbingQualityService.ValidateForSynthesis(
                Segments,
                RequireTranslatedDialogue,
                Characters
            );
            return true;
        }
        catch (InvalidOperationException exception)
        {
            RunStudioQualityCheck();
            SelectedStudioTab = 4;
            _snackbarManager.Notify(exception.Message);
            return false;
        }
    }

    [RelayCommand]
    private void RunStudioQualityCheck()
    {
        if (!CanUseSubscription())
            return;
        QualityIssues.Clear();
        foreach (
            var issue in DubbingQualityService.Analyze(
                Segments,
                Characters,
                RequireTranslatedDialogue
            )
        )
            QualityIssues.Add(issue);
        QualitySummary =
            Segments.Count == 0
                ? "Import subtitles or transcribe the movie to begin."
                : $"{Segments.Count} lines · {QualityIssues.Count(i => i.Severity == DubbingIssueSeverity.Error)} errors · "
                    + $"{QualityIssues.Count(i => i.Severity == DubbingIssueSeverity.Warning)} warnings · "
                    + $"{QualityIssues.Count(i => i.Severity == DubbingIssueSeverity.Info)} notes. Listen to scene transitions and overlapping voices before delivery.";
        QualityReportNeedsRefresh = false;
    }

    [RelayCommand]
    private void GoToQualityIssue(DubbingQualityIssue? issue)
    {
        if (!CanUseSubscription() || issue == null || !Segments.Contains(issue.Segment))
            return;
        SelectedStudioTab = 0;
        SelectSegment(issue.Segment);
        SeekTimeline(issue.Segment.StartSeconds);
    }
}
