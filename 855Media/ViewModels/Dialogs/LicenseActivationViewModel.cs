using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using _855Media.Core.Licensing;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Services;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Dialogs;

public partial class LicenseActivationViewModel : DialogViewModelBase
{
    private readonly ILicenseService _licenseService;
    private readonly SettingsService _settingsService;
    private readonly SnackbarManager _snackbarManager;
    private readonly DialogManager _dialogManager;

    [ObservableProperty]
    private string _licenseKeyInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public LicenseActivationViewModel(
        ILicenseService licenseService,
        SettingsService settingsService,
        SnackbarManager snackbarManager,
        DialogManager dialogManager,
        LocalizationManager localizationManager
    )
    {
        _licenseService = licenseService;
        _settingsService = settingsService;
        _snackbarManager = snackbarManager;
        _dialogManager = dialogManager;
        LocalizationManager = localizationManager;

        UpdateStatusProperties();
    }

    public LocalizationManager LocalizationManager { get; }

    public string MachineFingerprint => _licenseService.MachineFingerprint;
    public string PurchaseUrl => _settingsService.PurchaseUrl;

    public bool IsActivated => _licenseService.IsProActive;
    public bool IsTrial => _licenseService.IsTrialActive;
    public int TrialDaysRemaining => _licenseService.TrialDaysRemaining;

    public string StatusBadgeText =>
        _licenseService.Status switch
        {
            LicenseStatus.Active => "PRO ACTIVATED",
            LicenseStatus.Trial => $"TRIAL ({_licenseService.TrialDaysRemaining} days remaining)",
            LicenseStatus.Expired => "TRIAL EXPIRED",
            LicenseStatus.HardwareMismatch => "DEVICE MISMATCH",
            _ => "UNLICENSED",
        };

    public string LicenseDetailsText =>
        _licenseService.CurrentLicense is { } lic
            ? $"Licensed to: {lic.CustomerName} ({lic.CustomerEmail})\nType: {lic.Type} | Expires: {(lic.ExpiresAt.HasValue ? lic.ExpiresAt.Value.ToLocalTime().ToString("yyyy-MM-dd") : "Never (Lifetime)")}"
        : IsTrial ? $"You are using the 7-day free trial. {TrialDaysRemaining} days left."
        : "No active license detected. Please purchase a license or enter your key below.";

    private void UpdateStatusProperties()
    {
        OnPropertyChanged(nameof(IsActivated));
        OnPropertyChanged(nameof(IsTrial));
        OnPropertyChanged(nameof(TrialDaysRemaining));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(LicenseDetailsText));
    }

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseKeyInput))
        {
            StatusMessage = "Please enter your license key or signed license token.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Verifying license...";

        try
        {
            var trimmedInput = LicenseKeyInput.Trim();

            // Check if input is a signed token or a license key
            var result = trimmedInput.Contains('.')
                ? _licenseService.ActivateWithToken(trimmedInput)
                : await _licenseService.ActivateWithKeyOnlineAsync(
                    trimmedInput,
                    _settingsService.LicenseActivationApiUrl
                );

            if (result.Success && result.Payload is not null)
            {
                _settingsService.LicenseToken = trimmedInput;
                _settingsService.Save();

                StatusMessage =
                    "License activated successfully! Thank you for supporting 855Media.";
                _snackbarManager.Notify("855Media Pro successfully activated!");
                UpdateStatusProperties();
                Close(true);
            }
            else
            {
                StatusMessage = $"Activation failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Deactivate()
    {
        _licenseService.Deactivate();
        _settingsService.LicenseToken = null;
        _settingsService.Save();

        StatusMessage = "License removed from this machine.";
        _snackbarManager.Notify("License deactivated.");
        UpdateStatusProperties();
    }

    [RelayCommand]
    private async Task CopyMachineFingerprintAsync()
    {
        if (
            Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow?.Clipboard is { } clipboard
        )
        {
            await clipboard.SetTextAsync(MachineFingerprint);
            _snackbarManager.Notify("Device ID copied to clipboard!");
        }
    }

    [RelayCommand]
    private void OpenPurchasePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = PurchaseUrl, UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private async Task BrowseLicenseFileAsync()
    {
        var fileTypes = new[]
        {
            new FilePickerFileType("855Media License Files") { Patterns = ["*.lic", "*.txt"] },
            FilePickerFileTypes.All,
        };

        var filePath = await _dialogManager.PromptOpenFilePathAsync(fileTypes);
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        var content = (await File.ReadAllTextAsync(filePath)).Trim();
        if (!string.IsNullOrWhiteSpace(content))
        {
            LicenseKeyInput = content;
            await ActivateAsync();
        }
    }

    [RelayCommand]
    private void CloseDialog() => Close(false);
}
