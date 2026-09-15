using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using _855Media.Framework;
using _855Media.ViewModels.Components;

namespace _855Media.Services;

public class HistoryService
{
    private readonly string _historyFilePath;
    private readonly List<HistoryRecord> _records = [];
    private readonly object _lock = new();

    public ObservableCollection<HistoryRecord> Records { get; } = [];

    public HistoryService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Program.Name
        );
        Directory.CreateDirectory(appDataDir);
        _historyFilePath = Path.Combine(appDataDir, "history.json");

        Load();
    }

    public void Load()
    {
        lock (_lock)
        {
            _records.Clear();
            Records.Clear();

            if (!File.Exists(_historyFilePath))
                return;

            try
            {
                var json = File.ReadAllText(_historyFilePath);
                var items = JsonSerializer.Deserialize<List<HistoryRecord>>(json);
                if (items is not null)
                {
                    _records.AddRange(items.OrderByDescending(r => r.DownloadedAt));
                    foreach (var record in _records)
                    {
                        Records.Add(record);
                    }
                }
            }
            catch
            {
                // Silently ignore corrupt history file
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(
                    _records,
                    new JsonSerializerOptions { WriteIndented = true }
                );
                File.WriteAllText(_historyFilePath, json);
            }
            catch
            {
                // Silently handle save exceptions
            }
        }
    }

    public void AddOrUpdateRecord(HistoryRecord record)
    {
        lock (_lock)
        {
            var existingIndex = _records.FindIndex(r =>
                r.Url == record.Url && r.FilePath == record.FilePath
            );
            if (existingIndex >= 0)
            {
                _records[existingIndex] = record;
            }
            else
            {
                _records.Insert(0, record);
            }

            SyncRecordsToCollection();
            Save();
        }
    }

    public void RemoveRecord(string id)
    {
        lock (_lock)
        {
            _records.RemoveAll(r => r.Id == id);
            SyncRecordsToCollection();
            Save();
        }
    }

    public void ClearHistory()
    {
        lock (_lock)
        {
            _records.Clear();
            Records.Clear();
            Save();
        }
    }

    public async Task ExportAsync(string destinationFilePath, bool asCsv = false)
    {
        List<HistoryRecord> snapshot;
        lock (_lock)
        {
            snapshot = _records.ToList();
        }

        if (asCsv)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Title,Author,Url,FilePath,Source,Status,DownloadedAt,ErrorMessage");
            foreach (var r in snapshot)
            {
                string EscapeCsv(string? val) => $"\"{val?.Replace("\"", "\"\"") ?? ""}\"";
                sb.AppendLine(
                    $"{EscapeCsv(r.Title)},{EscapeCsv(r.Author)},{EscapeCsv(r.Url)},{EscapeCsv(r.FilePath)},{EscapeCsv(r.Source)},{r.Status},{r.DownloadedAt:o},{EscapeCsv(r.ErrorMessage)}"
                );
            }
            await File.WriteAllTextAsync(destinationFilePath, sb.ToString(), Encoding.UTF8);
        }
        else
        {
            var json = JsonSerializer.Serialize(
                snapshot,
                new JsonSerializerOptions { WriteIndented = true }
            );
            await File.WriteAllTextAsync(destinationFilePath, json, Encoding.UTF8);
        }
    }

    private void SyncRecordsToCollection()
    {
        Records.Clear();
        foreach (var record in _records.OrderByDescending(r => r.DownloadedAt))
        {
            Records.Add(record);
        }
    }
}
