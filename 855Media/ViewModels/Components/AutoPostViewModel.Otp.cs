using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private byte[]? _addAccountOtpSecret;
    private DispatcherTimer? _addAccountOtpTimer;
    private DateTimeOffset _addAccountOtpPreviewUntil;
    private PFlowOtpInbox? _addAccountOtpInbox;
    private PFlowOtpRequest? _addAccountOtpRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAddAccountOtpCode))]
    [property: JsonIgnore]
    private string _addAccountOtpCode = "";

    [JsonIgnore]
    public bool HasAddAccountOtpCode => AddAccountOtpCode.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddAccountOtpPasswordChar))]
    [property: JsonIgnore]
    private bool _showAddAccountOtp;

    [JsonIgnore]
    public char AddAccountOtpPasswordChar => ShowAddAccountOtp ? '\0' : '●';

    [ObservableProperty]
    private string _addAccountOtpStatus =
        "Codes are generated locally from the third field. SMS/email codes require the message you receive.";

    [ObservableProperty]
    private bool _isWaitingForAddAccountOtp;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _addAccountReceivedOtp = "";

    partial void OnAddAccountLineChanged(string value) => ClearAddAccountOtp();

    [RelayCommand]
    private void GenerateAddAccountOtp()
    {
        if (_addAccountTarget == null || IsAddingAccount || !IsAddAccountOpen)
            return;
        ClearAddAccountOtp();
        try
        {
            var target = _addAccountTarget;
            using var input = PFlowAddAccountInput.Parse(
                AddAccountLine,
                target.DeviceIndex,
                target.DeviceName,
                target.InstanceIdentity,
                target.AppPackage
            );
            if (input.Credential.TwoFactorKind != PFlowTwoFactorKind.TotpSecret)
                throw new FormatException(
                    "Enter the Base32 authenticator secret in the third field. An SMS/email code is entered separately when requested."
                );
            _addAccountOtpSecret = (byte[])input.Credential.TwoFactor.Clone();
            _addAccountOtpPreviewUntil = DateTimeOffset.UtcNow.AddMinutes(2);
            UpdateAddAccountOtp();
            _addAccountOtpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _addAccountOtpTimer.Tick += TickAddAccountOtp;
            _addAccountOtpTimer.Start();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            ClearAddAccountOtp();
            AddAccountOtpStatus = ex.Message;
        }
    }

    private void TickAddAccountOtp(object? sender, EventArgs e) => UpdateAddAccountOtp();

    private void UpdateAddAccountOtp()
    {
        if (_addAccountOtpSecret == null)
            return;
        var now = DateTimeOffset.UtcNow;
        if (!IsAddAccountOpen || now >= _addAccountOtpPreviewUntil)
        {
            ClearAddAccountOtp();
            AddAccountOtpStatus = "Code preview cleared. Generate again when ready.";
            return;
        }
        using var code = PFlowOtpCode.Authenticator(
            Encoding.UTF8.GetString(_addAccountOtpSecret),
            now
        );
        AddAccountOtpCode = Encoding.ASCII.GetString(code.Value);
        var seconds = (int)Math.Ceiling((code.ExpiresUtc - now).TotalSeconds);
        AddAccountOtpStatus =
            seconds < 8
                ? $"{seconds}s remaining · wait for the next code before entering it."
                : $"{seconds}s remaining · refreshes every 30s. Use only for this account’s authenticator challenge. Check Windows time if rejected.";
    }

    [RelayCommand]
    private void ClearAddAccountOtp()
    {
        if (_addAccountOtpTimer != null)
        {
            _addAccountOtpTimer.Stop();
            _addAccountOtpTimer.Tick -= TickAddAccountOtp;
            _addAccountOtpTimer = null;
        }
        if (_addAccountOtpSecret != null)
            CryptographicOperations.ZeroMemory(_addAccountOtpSecret);
        _addAccountOtpSecret = null;
        AddAccountOtpCode = "";
        ShowAddAccountOtp = false;
        AddAccountOtpStatus =
            "Codes are generated locally from the third field. SMS/email codes require the message you receive.";
    }

    private void ShowAddAccountOtpRequest(PFlowOtpInbox inbox, PFlowOtpRequest? request)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_addAccountOtpInbox != inbox)
                return;
            _addAccountOtpRequest = request;
            AddAccountReceivedOtp = "";
            IsWaitingForAddAccountOtp = request != null;
            if (request != null)
                AddAccountOtpStatus =
                    $"Enter the {request.Digits}-digit {request.Method} code for this login. It is not saved. Waiting up to 2 minutes; Stop login cancels.";
        });
    }

    [RelayCommand]
    private void SubmitAddAccountOtp()
    {
        var request = _addAccountOtpRequest;
        if (
            !IsAddingAccount
            || !IsWaitingForAddAccountOtp
            || request == null
            || _addAccountOtpInbox == null
        )
            return;
        var value = AddAccountReceivedOtp;
        AddAccountReceivedOtp = "";
        if (
            _addAccountOtpInbox.TrySubmit(
                request.Binding,
                request.ChallengeId,
                value,
                out var status
            )
        )
            IsWaitingForAddAccountOtp = false;
        AddAccountOtpStatus = status;
    }

    private void EndAddAccountOtp()
    {
        StopAccountMail();
        _addAccountOtpInbox?.Dispose();
        _addAccountOtpInbox = null;
        _addAccountOtpRequest = null;
        AddAccountReceivedOtp = "";
        IsWaitingForAddAccountOtp = false;
        ClearAddAccountOtp();
    }
}
