using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using _855Media.Utils.Extensions;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerKit.Extensions;

namespace _855Media.ViewModels.Components;

public partial class HistoryViewModel : ViewModelBase
{
    private readonly HistoryService _historyService;
    private readonly ViewModelManager _viewModelManager;
    private readonly DialogManager _dialogManager;
    private readonly SnackbarManager _snackbarManager;
    private DashboardViewModel? _dashboardViewModel;

    public HistoryViewModel(
        HistoryService historyService,
        ViewModelManager viewModelManager,
        DialogManager dialogManager,
        SnackbarManager snackbarManager,
        LocalizationManager localizationManager
    )
    {
        _historyService = historyService;
        _viewModelManager = viewModelManager;
        _dialogManager = dialogManager;
        _snackbarManager = snackbarManager;
        LocalizationManager = localizationManager;

        _historyService.Records.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(FilteredRecords));
            OnPropertyChanged(nameof(HasRecords));
        };
    }

    public void Initialize(DashboardViewModel dashboardViewModel)
    {
        _dashboardViewModel = dashboardViewModel;
    }

    public LocalizationManager LocalizationManager { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredRecords))]
    public partial string? SearchQuery { get; set; }

    public bool HasRecords => _historyService.Records.Count > 0;

    public IEnumerable<HistoryRecord> FilteredRecords
    {
        get
        {
            var records = _historyService.Records;
            if (string.IsNullOrWhiteSpace(SearchQuery))
                return records;

            var query = SearchQuery.Trim();
            return records.Where(r =>
                r.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Author.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Url.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
            );
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!HasRecords)
            return;

        var dialog = _viewModelManager.GetMessageBoxViewModel(
            LocalizationManager.ClearHistoryTitle,
            LocalizationManager.ClearHistoryMessage,
            LocalizationManager.ClearHistoryButton,
            LocalizationManager.CancelButton
        );

        if (await _dialogManager.ShowDialogAsync(dialog) == true)
        {
            _historyService.ClearHistory();
            _snackbarManager.Notify(LocalizationManager.HistoryClearedSnackbar);
        }
    }

    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        if (!HasRecords)
            return;

        var fileTypes = new[]
        {
            new FilePickerFileType("CSV Document (*.csv)") { Patterns = new[] { "*.csv" } },
            new FilePickerFileType("JSON Document (*.json)") { Patterns = new[] { "*.json" } },
        };

        var filePath = await _dialogManager.PromptSaveFilePathAsync(fileTypes, "history.csv");

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            bool isCsv = filePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            await _historyService.ExportAsync(filePath, isCsv);
            _snackbarManager.Notify(LocalizationManager.HistoryExportedSnackbar);
        }
    }

    [RelayCommand]
    private void RemoveRecord(HistoryRecord record)
    {
        if (record is null)
            return;
        _historyService.RemoveRecord(record.Id);
    }

    [RelayCommand]
    private async Task RedownloadAsync(HistoryRecord record)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.Url) || _dashboardViewModel is null)
            return;

        _dashboardViewModel.Query = record.Url;
        await _dashboardViewModel.ProcessQueryCommand.ExecuteAsync(null);
        _dashboardViewModel.SelectedTab = DashboardTab.Manager;
    }

    [RelayCommand]
    private async Task OpenFileAsync(HistoryRecord record)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.FilePath))
            return;

        try
        {
            if (File.Exists(record.FilePath))
            {
                Process.StartShellExecute(record.FilePath);
            }
            else
            {
                await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetMessageBoxViewModel(
                        LocalizationManager.ErrorTitle,
                        LocalizationManager.FileNotFoundMessage
                    )
                );
            }
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(LocalizationManager.ErrorTitle, ex.Message)
            );
        }
    }

    [RelayCommand]
    private async Task ShowFileAsync(HistoryRecord record)
    {
        if (record is null || string.IsNullOrWhiteSpace(record.FilePath))
            return;

        try
        {
            if (File.Exists(record.FilePath) && OperatingSystem.IsWindows())
            {
                Process.Start("explorer", ["/select,", record.FilePath]);
            }
            else
            {
                await _dialogManager.ShowDialogAsync(
                    _viewModelManager.GetMessageBoxViewModel(
                        LocalizationManager.ErrorTitle,
                        LocalizationManager.FileNotFoundMessage
                    )
                );
            }
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.GetMessageBoxViewModel(LocalizationManager.ErrorTitle, ex.Message)
            );
        }
    }
}
