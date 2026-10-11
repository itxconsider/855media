using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [ObservableProperty]
    [property: JsonIgnore]
    private string _mailHost = "imap.yandex.com";

    [ObservableProperty]
    [property: JsonIgnore]
    private int _mailPort = 993;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _mailEmail = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private string _mailAppPassword = "";

    [ObservableProperty]
    [property: JsonIgnore]
    private string _mailStatus =
        "Connect a mailbox dedicated to this Facebook account. IMAP access and an app password must be enabled.";

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isMailBusy;
    private DateTimeOffset _mailLoginStartedUtc;
    private CancellationTokenSource? _mailCancellation;

    [RelayCommand]
    private async Task SaveAccountMailboxAsync()
    {
        if (_addAccountTarget == null || IsAddingAccount || IsMailBusy)
            return;
        IsMailBusy = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _mailCancellation = cancellation;
        try
        {
            using var input = PFlowAddAccountInput.Parse(
                AddAccountLine,
                _addAccountTarget.DeviceIndex,
                _addAccountTarget.DeviceName,
                _addAccountTarget.InstanceIdentity,
                _addAccountTarget.AppPackage
            );
            var mailbox = new PFlowMailbox(
                MailHost.Trim(),
                MailPort,
                MailEmail.Trim(),
                MailAppPassword
            );
            await new PFlowMailService().TestAsync(mailbox, cancellation.Token);
            await new PFlowMailboxStore().SaveAsync(input.Binding, mailbox, cancellation.Token);
            MailStatus =
                "IMAP connection verified. Mailbox saved encrypted for this UID and LD/app binding.";
        }
        catch (OperationCanceledException)
        {
            MailStatus = "Mailbox connection stopped or timed out.";
        }
        catch
        {
            MailStatus =
                "Mailbox connection failed. Check the account line, IMAP settings and app password; OAuth-only providers require a separate connector.";
        }
        finally
        {
            MailAppPassword = "";
            IsMailBusy = false;
            _mailCancellation = null;
        }
    }

    [RelayCommand]
    private async Task ReceiveAccountMailCodeAsync()
    {
        var request = _addAccountOtpRequest;
        if (
            request == null
            || !IsWaitingForAddAccountOtp
            || IsMailBusy
            || _addAccountCancellation == null
        )
            return;
        IsMailBusy = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _addAccountCancellation.Token
        );
        _mailCancellation = cancellation;
        try
        {
            MailStatus = "Waiting up to 2 minutes for a new matching Facebook verification email…";
            var mailbox = await new PFlowMailboxStore().LoadAsync(
                request.Binding,
                cancellation.Token
            );
            var code = await new PFlowMailService().WaitForCodeAsync(
                mailbox,
                request,
                request.Binding,
                _mailLoginStartedUtc,
                cancellation.Token
            );
            if (_addAccountOtpRequest == request && IsWaitingForAddAccountOtp)
            {
                AddAccountReceivedOtp = code;
                MailStatus =
                    "New email code received. Choose Use received code to submit it to the current challenge.";
            }
            else
                MailStatus = "The challenge changed. The received code was discarded.";
        }
        catch (OperationCanceledException)
        {
            MailStatus = "Email wait stopped or timed out. You can enter a code manually.";
        }
        catch
        {
            MailStatus =
                "No unambiguous email code could be received. Check the saved mailbox or enter the code manually.";
        }
        finally
        {
            IsMailBusy = false;
            _mailCancellation = null;
        }
    }

    [RelayCommand]
    private void StopAccountMail() => _mailCancellation?.Cancel();
}
