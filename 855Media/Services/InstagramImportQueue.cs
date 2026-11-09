using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;

namespace _855Media.Services;

public sealed record InstagramImportQueue(
    Func<IReadOnlyList<VideoInfo>, CancellationToken, Task<int>> EnqueueAsync,
    Func<string, bool> CanResumePost,
    Func<string, IReadOnlyList<VideoInfo>, CancellationToken, Task> RememberPostAsync
)
{
    public static bool IsInDestination(string? path, string destination)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path)
                && string.Equals(
                    Path.GetDirectoryName(Path.GetFullPath(path)),
                    destination,
                    StringComparison.OrdinalIgnoreCase
                );
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool HasCompletedFile(
        HistoryRecord record,
        string pageUrl,
        InstagramImportItem item,
        string destination,
        bool saveCaption
    )
    {
        if (
            record.Status != _855Media.ViewModels.Components.DownloadStatus.Completed
            || record.SourceItemId != item.Id
            || record.Source
                != (item.IsPhoto ? VideoSource.InstagramPhoto : VideoSource.Instagram).ToString()
            || InstagramBrowserCollection.GetPostCode(pageUrl) is not { } code
            || InstagramBrowserCollection.GetPostCode(record.Url) != code
            || record.FileSizeBytes is not > 0
            || !IsInDestination(record.FilePath, destination)
        )
            return false;
        try
        {
            var file = new FileInfo(record.FilePath);
            return file.Exists
                && file.Length == record.FileSizeBytes
                && (
                    !saveCaption
                    || !item.HasCaption
                    || File.Exists(Path.ChangeExtension(record.FilePath, ".txt"))
                );
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
