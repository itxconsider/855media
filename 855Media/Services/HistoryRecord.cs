using System;
using System.Text.Json.Serialization;
using _855Media.ViewModels.Components;

namespace _855Media.Services;

public class HistoryRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Completed;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset DownloadedAt { get; set; } = DateTimeOffset.Now;
    public long? FileSizeBytes { get; set; }

    [JsonIgnore]
    public string FormattedDownloadedAt => DownloadedAt.LocalDateTime.ToString("g");

    [JsonIgnore]
    public string FormattedFileSize
    {
        get
        {
            if (FileSizeBytes is null or <= 0)
                return string.Empty;
            double bytes = FileSizeBytes.Value;
            if (bytes >= 1024 * 1024 * 1024)
                return $"{bytes / (1024 * 1024 * 1024):F2} GB";
            if (bytes >= 1024 * 1024)
                return $"{bytes / (1024 * 1024):F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024:F2} KB";
            return $"{bytes} B";
        }
    }
}
