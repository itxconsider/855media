using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class PageMediaPathEdit : ObservableObject
{
    public string Label { get; }
    public string Key { get; }
    public bool FolderOnly { get; }
    public string[] Extensions { get; }

    [ObservableProperty]
    private bool _apply;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private bool _clear;
    public string Preview =>
        Clear ? "Remove the saved path" : Describe(Path, Extensions, FolderOnly);
    public IAsyncRelayCommand BrowseFileCommand { get; }
    public IAsyncRelayCommand BrowseFolderCommand { get; }
    public bool CanBrowseFile => !FolderOnly;

    partial void OnPathChanged(string value)
    {
        Apply = true;
        Clear = false;
        OnPropertyChanged(nameof(Preview));
    }

    partial void OnClearChanged(bool value)
    {
        if (value)
            Apply = true;
        OnPropertyChanged(nameof(Preview));
    }

    public PageMediaPathEdit(
        string key,
        string label,
        bool folderOnly,
        string[] extensions,
        Func<PageMediaPathEdit, bool, Task> browse
    )
    {
        Key = key;
        Label = label;
        FolderOnly = folderOnly;
        Extensions = extensions;
        BrowseFileCommand = new AsyncRelayCommand(() => browse(this, false));
        BrowseFolderCommand = new AsyncRelayCommand(() => browse(this, true));
    }

    public static string Normalize(string? value) =>
        Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"', '\''));

    public static string Describe(string? value, string[] extensions, bool folderOnly = false)
    {
        var path = Normalize(value);
        if (path.Length == 0)
            return "No media path configured";
        if (!System.IO.Path.IsPathFullyQualified(path))
            return "Use an absolute file or folder path";
        try
        {
            if (Directory.Exists(path))
                return $"{Directory.EnumerateFiles(path).Count(f => extensions.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))} matching files • folder (top level)";
            if (File.Exists(path))
                return folderOnly ? "Select a folder for group media"
                    : extensions.Contains(
                        System.IO.Path.GetExtension(path),
                        StringComparer.OrdinalIgnoreCase
                    )
                        ? "1 matching file"
                    : "Unsupported file type";
            return "Path does not exist on this computer";
        }
        catch (Exception ex)
        {
            return $"Cannot read path: {ex.Message}";
        }
    }

    public string? Validate()
    {
        if (Clear)
            return null;
        var description = Describe(Path, Extensions, FolderOnly);
        return description.Contains("matching file", StringComparison.Ordinal)
            ? null
            : $"{Label}: {description}";
    }
}

public partial class AutoPostViewModel
{
    [ObservableProperty]
    private string _pageConfigError = string.Empty;

    [ObservableProperty]
    private bool _isBulkPathsOpen;

    [ObservableProperty]
    private string _bulkPathsTargets = string.Empty;

    [ObservableProperty]
    private string _bulkPathsError = string.Empty;

    [ObservableProperty]
    private bool _isSavingBulkPaths;

    [ObservableProperty]
    private bool _bulkApplyCaption;

    [ObservableProperty]
    private string _bulkCaptionSource = "Filename";
    public string[] BulkCaptionSources { get; } =
    ["Filename", "Matching .txt file", "Configured caption"];
    public ObservableCollection<PageMediaPathEdit> BulkPathEdits { get; } = [];
    private List<FacebookPageTargetViewModel> _bulkPathTargets = [];

    [RelayCommand]
    private void OpenBulkPaths()
    {
        if (IsRunning || IsBusy)
        {
            _snackbarManager.Notify("Stop the current operation before editing media paths.");
            return;
        }
        _bulkPathTargets = DisplayedManagePages.Where(p => p.IsSelected).ToList();
        if (_bulkPathTargets.Count == 0)
        {
            _snackbarManager.Notify("Check the Pages or profiles whose paths you want to update.");
            return;
        }
        BulkPathsTargets =
            $"{_bulkPathTargets.Count} selected targets\n"
            + string.Join(" • ", _bulkPathTargets.Select(p => $"{p.DeviceName}: {p.PageName}"));
        BulkApplyCaption = false;
        BulkCaptionSource = "Filename";
        BulkPathsError = string.Empty;
        BulkPathEdits.Clear();
        BulkPathEdits.Add(
            new("VideoReelPath", "Reels", false, [".mp4", ".mov", ".webm"], BrowseBulkPathAsync)
        );
        BulkPathEdits.Add(
            new(
                "ImagePath",
                "Photos",
                false,
                [".jpg", ".jpeg", ".png", ".webp"],
                BrowseBulkPathAsync
            )
        );
        BulkPathEdits.Add(
            new(
                "VideoLongPath",
                "Long videos",
                false,
                [".mp4", ".mkv", ".mov"],
                BrowseBulkPathAsync
            )
        );
        BulkPathEdits.Add(
            new(
                "GroupPicturePath",
                "Group photos",
                true,
                [".jpg", ".jpeg", ".png", ".webp"],
                BrowseBulkPathAsync
            )
        );
        BulkPathEdits.Add(
            new(
                "GroupReelPath",
                "Group reels",
                true,
                [".mp4", ".mov", ".webm"],
                BrowseBulkPathAsync
            )
        );
        BulkPathEdits.Add(
            new(
                "GroupVideoPath",
                "Group videos",
                true,
                [".mp4", ".mkv", ".mov"],
                BrowseBulkPathAsync
            )
        );
        IsBulkPathsOpen = true;
    }

    private async Task BrowseBulkPathAsync(PageMediaPathEdit edit, bool folder)
    {
        if (IsSavingBulkPaths)
            return;
        try
        {
            var path = folder
                ? await _dialogManager.PromptDirectoryPathAsync(
                    PageMediaPathEdit.Normalize(edit.Path)
                )
                : await _dialogManager.PromptOpenFilePathAsync(
                    [
                        new FilePickerFileType(edit.Label)
                        {
                            Patterns = edit.Extensions.Select(e => "*" + e).ToArray(),
                        },
                    ],
                    PageMediaPathEdit.Normalize(edit.Path)
                );
            if (!string.IsNullOrWhiteSpace(path))
                edit.Path = PageMediaPathEdit.Normalize(path);
        }
        catch (Exception ex)
        {
            BulkPathsError = $"Unable to choose path: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseBulkPaths()
    {
        if (!IsSavingBulkPaths)
            IsBulkPathsOpen = false;
    }

    [RelayCommand]
    private async Task ApplyBulkPathsAsync()
    {
        if (IsSavingBulkPaths)
            return;
        if (IsRunning || IsBusy)
        {
            BulkPathsError = "Stop the current operation before saving media paths.";
            return;
        }
        var edits = BulkPathEdits.Where(e => e.Apply).ToList();
        if (edits.Count == 0 && !BulkApplyCaption)
        {
            BulkPathsError = "Choose a path or caption source to update.";
            return;
        }
        BulkPathsError = string.Join("\n", edits.Select(e => e.Validate()).Where(e => e != null));
        if (BulkPathsError.Length > 0)
            return;
        if (_bulkPathTargets.Any(p => !Pages.Contains(p)))
        {
            BulkPathsError = "The target list changed. Close and reopen this editor.";
            return;
        }
        var keys = edits
            .Select(e => e.Key)
            .Concat(
                BulkApplyCaption
                    ? new[] { "UseTitleFromTextFile", "UseCaptionFromFilename" }
                    : Array.Empty<string>()
            )
            .ToArray();
        var originals = _bulkPathTargets
            .SelectMany(p =>
                keys.Select(key =>
                    (
                        Page: p,
                        Property: typeof(FacebookPageTargetViewModel).GetProperty(key)!,
                        Value: typeof(FacebookPageTargetViewModel).GetProperty(key)!.GetValue(p)
                    )
                )
            )
            .ToList();
        IsSavingBulkPaths = true;
        IsBusy = true;
        try
        {
            foreach (var p in _bulkPathTargets)
            foreach (var e in edits)
                typeof(FacebookPageTargetViewModel)
                    .GetProperty(e.Key)!
                    .SetValue(p, e.Clear ? "" : PageMediaPathEdit.Normalize(e.Path));
            if (BulkApplyCaption)
                foreach (var p in _bulkPathTargets)
                {
                    p.UseTitleFromTextFile = BulkCaptionSource == "Matching .txt file";
                    p.UseCaptionFromFilename = BulkCaptionSource == "Filename";
                    p.SyncToModel();
                }
            await PersistPagesSnapshotAsync();
            RefreshDisplayedManagePages();
            IsBulkPathsOpen = false;
            _snackbarManager.Notify($"Media paths saved for {_bulkPathTargets.Count} targets.");
        }
        catch (Exception ex)
        {
            foreach (var item in originals)
            {
                item.Property.SetValue(item.Page, item.Value);
                item.Page.SyncToModel();
            }
            BulkPathsError = $"Could not save media paths: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            IsSavingBulkPaths = false;
        }
    }
}
