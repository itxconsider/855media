using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Utils.Extensions;
using Avalonia;
using Avalonia.Platform.Storage;
using DialogHostAvalonia;

namespace _855Media.Framework;

public class DialogManager : IDisposable
{
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    public async Task<T?> ShowDialogAsync<T>(DialogViewModelBase<T> dialog)
    {
        await _dialogLock.WaitAsync();
        try
        {
            await DialogHost.Show(
                dialog,
                // It's fine to await in a void method here because it's an event handler
                // ReSharper disable once AsyncVoidLambda
                async (object _, DialogOpenedEventArgs args) =>
                {
                    await dialog.WaitForCloseAsync();

                    try
                    {
                        args.Session.Close();
                    }
                    catch (InvalidOperationException)
                    {
                        // Dialog host is already processing a close operation
                    }
                }
            );

            // Yield to allow DialogHost to fully reset its state before
            // another dialog is shown (e.g. when dialogs are shown sequentially)
            await Task.Yield();

            return dialog.DialogResult;
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    public async Task<string?> PromptOpenFilePathAsync(
        IReadOnlyList<FilePickerFileType>? fileTypes = null
    )
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { FileTypeFilter = fileTypes, AllowMultiple = false }
        );

        var file = result.FirstOrDefault();
        return ResolveLocalPath(file);
    }

    public async Task<IReadOnlyList<string>> PromptOpenFilePathsAsync(
        IReadOnlyList<FilePickerFileType>? fileTypes = null
    )
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { FileTypeFilter = fileTypes, AllowMultiple = true }
        );

        return result
            .Select(ResolveLocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToArray();
    }

    public async Task<string?> PromptSaveFilePathAsync(
        IReadOnlyList<FilePickerFileType>? fileTypes = null,
        string defaultFilePath = ""
    )
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        var dirPart = Path.GetDirectoryName(defaultFilePath);
        var filePart = Path.GetFileName(defaultFilePath);
        var safeFileName = _855Media.Core.Utils.FileUtils.SanitizeFileName(
            filePart,
            fallback: "video"
        );

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                FileTypeChoices = fileTypes,
                SuggestedFileName = safeFileName,
                DefaultExtension = Path.GetExtension(defaultFilePath).TrimStart('.'),
            }
        );

        var path = ResolveLocalPath(file);
        return !string.IsNullOrWhiteSpace(path)
            ? _855Media.Core.Utils.FileUtils.SanitizeFilePath(path)
            : null;
    }

    public async Task<string?> PromptDirectoryPathAsync(string defaultDirPath = "")
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
                    defaultDirPath
                ),
            }
        );

        var directory = result.FirstOrDefault();
        if (directory is null)
            return null;

        return ResolveLocalPath(directory);
    }

    private static string? ResolveLocalPath(IStorageItem? item)
    {
        if (item is null)
            return null;

        if (item.TryGetLocalPath() is { } localPath && !string.IsNullOrWhiteSpace(localPath))
            return localPath;

        if (item.Path is { } uri)
        {
            if (uri.IsFile)
                return uri.LocalPath;

            return Uri.UnescapeDataString(uri.AbsolutePath);
        }

        return null;
    }

    public void Dispose() => _dialogLock.Dispose();
}
