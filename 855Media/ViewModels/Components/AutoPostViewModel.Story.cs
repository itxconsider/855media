using System;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [RelayCommand]
    private async Task BrowseStoryFileAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var path = await _dialogManager.PromptOpenFilePathAsync(
                [
                    new FilePickerFileType("Story photos and videos")
                    {
                        Patterns = FacebookPageTarget
                            .StoryExtensions.Select(e => "*" + e)
                            .ToArray(),
                    },
                ],
                SelectedPageForEdit.StoryPath
            );
            if (!string.IsNullOrWhiteSpace(path))
                SelectedPageForEdit.StoryPath = PageMediaPathEdit.Normalize(path);
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify("Could not select Story media: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task BrowseStoryFolderAsync()
    {
        if (SelectedPageForEdit == null)
            return;
        try
        {
            var path = await _dialogManager.PromptDirectoryPathAsync(SelectedPageForEdit.StoryPath);
            if (!string.IsNullOrWhiteSpace(path))
                SelectedPageForEdit.StoryPath = PageMediaPathEdit.Normalize(path);
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify("Could not select Story folder: " + ex.Message);
        }
    }
}
