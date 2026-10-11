using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowMailbox(string Host, int Port, string Email, string AppPassword)
{
    public void Validate()
    {
        if (
            Uri.CheckHostName(Host) != UriHostNameType.Dns
            || Port is < 1 or > 65535
            || !MailAddress.TryCreate(Email, out var parsed)
            || parsed.Address != Email
            || string.IsNullOrWhiteSpace(AppPassword)
            || AppPassword.Length > 4096
        )
            throw new FormatException(
                "Enter an IMAP host, TLS port, mailbox email and app password."
            );
    }

    public override string ToString() => "Protected IMAP mailbox";
}

public sealed class PFlowMailboxStore(string? directory = null)
{
    private readonly string _directory =
        directory
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media",
            "mailboxes"
        );

    private string FileName(PFlowAccountBinding binding)
    {
        binding.Validate();
        return Path.Combine(_directory, binding.AccountKey + ".bin");
    }

    public async Task SaveAsync(
        PFlowAccountBinding binding,
        PFlowMailbox mailbox,
        CancellationToken ct
    )
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Protected mailboxes require Windows.");
        mailbox.Validate();
        var file = FileName(binding);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(mailbox);
        try
        {
            var protectedBytes = ProtectedData.Protect(
                bytes,
                Encoding.UTF8.GetBytes(binding.AccountKey),
                DataProtectionScope.CurrentUser
            );
            Directory.CreateDirectory(_directory);
            var draft = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(draft, protectedBytes, ct);
                ct.ThrowIfCancellationRequested();
                File.Move(draft, file, true);
            }
            finally
            {
                if (File.Exists(draft))
                    File.Delete(draft);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public async Task<PFlowMailbox> LoadAsync(PFlowAccountBinding binding, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Protected mailboxes require Windows.");
        var file = FileName(binding);
        if (!File.Exists(file))
            throw new InvalidOperationException(
                "Connect and save a mailbox for this account first."
            );
        if (new FileInfo(file).Length > 65536)
            throw new InvalidOperationException("Mailbox configuration is invalid.");
        var encrypted = await File.ReadAllBytesAsync(file, ct);
        var bytes = ProtectedData.Unprotect(
            encrypted,
            Encoding.UTF8.GetBytes(binding.AccountKey),
            DataProtectionScope.CurrentUser
        );
        try
        {
            var mailbox =
                JsonSerializer.Deserialize<PFlowMailbox>(bytes)
                ?? throw new InvalidOperationException("Mailbox configuration is invalid.");
            mailbox.Validate();
            return mailbox;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

public sealed class PFlowMailService
{
    private static async Task<ImapClient> ConnectAsync(PFlowMailbox mailbox, CancellationToken ct)
    {
        mailbox.Validate();
        var client = new ImapClient { Timeout = 15000 };
        try
        {
            await client.ConnectAsync(
                mailbox.Host,
                mailbox.Port,
                SecureSocketOptions.SslOnConnect,
                ct
            );
            await client.AuthenticateAsync(mailbox.Email, mailbox.AppPassword, ct);
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task TestAsync(PFlowMailbox mailbox, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var client = await ConnectAsync(mailbox, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }

    internal static string? ExtractCode(MimeMessage message, string email, int digits)
    {
        if (
            digits is not (6 or 8)
            || !message.From.Mailboxes.Any(m =>
                m.Address.Equals("security@facebookmail.com", StringComparison.OrdinalIgnoreCase)
                || m.Address.Equals("noreply@facebookmail.com", StringComparison.OrdinalIgnoreCase)
            )
            || !message
                .To.Mailboxes.Concat(message.Cc.Mailboxes)
                .Any(m => m.Address.Equals(email, StringComparison.OrdinalIgnoreCase))
        )
            return null;
        var content =
            (message.Subject ?? "")
            + "\n"
            + (
                message.TextBody
                ?? System.Net.WebUtility.HtmlDecode(
                    Regex.Replace(message.HtmlBody ?? "", "<[^>]*>", " ")
                )
            );
        if (
            content.Length > 262144
            || !Regex.IsMatch(
                content,
                @"\b(?:login code|security code|confirmation code|verification code|log in code)\b",
                RegexOptions.IgnoreCase
            )
        )
            return null;
        var codes = Regex
            .Matches(content, @"(?<!\d)\d{" + digits + @"}(?!\d)")
            .Select(m => m.Value)
            .Distinct()
            .ToArray();
        return codes.Length == 1 ? codes[0] : null;
    }

    public async Task<string> WaitForCodeAsync(
        PFlowMailbox mailbox,
        PFlowOtpRequest request,
        PFlowAccountBinding binding,
        DateTimeOffset loginStartedUtc,
        CancellationToken ct
    )
    {
        binding.Validate();
        if (
            request.Binding != binding
            || string.IsNullOrWhiteSpace(request.ChallengeId)
            || request.Digits is not (6 or 8)
            || request.ObservedUtc > DateTimeOffset.UtcNow
            || request.ObservedUtc < DateTimeOffset.UtcNow.AddMinutes(-2)
            || loginStartedUtc > request.ObservedUtc
        )
            throw new InvalidOperationException(
                "The email challenge is stale or belongs to another account."
            );
        if (request.Method != PFlowOtpMethod.Email)
            throw new InvalidOperationException(
                "The current challenge does not request an email code."
            );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        using var client = await ConnectAsync(mailbox, token);
        var lowerBound =
            request.NotBeforeUtc > loginStartedUtc ? request.NotBeforeUtc.Value : loginStartedUtc;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await client.NoOpAsync(token);
            var ids = await client.Inbox.SearchAsync(
                SearchQuery.DeliveredAfter(lowerBound.UtcDateTime.Date),
                token
            );
            var recent = ids.Reverse().Take(30).ToArray();
            var codes = new System.Collections.Generic.HashSet<string>();
            if (recent.Length > 0)
            {
                var summaries = await client.Inbox.FetchAsync(
                    recent,
                    MessageSummaryItems.InternalDate | MessageSummaryItems.Size,
                    token
                );
                foreach (
                    var summary in summaries.Where(s =>
                        s.InternalDate >= lowerBound
                        && s.InternalDate <= DateTimeOffset.UtcNow.AddSeconds(30)
                        && s.Size <= 262144
                    )
                )
                {
                    var message = await client.Inbox.GetMessageAsync(summary.UniqueId, token);
                    var code = ExtractCode(message, mailbox.Email, request.Digits);
                    if (code != null)
                        codes.Add(code);
                }
            }
            if (codes.Count > 1)
                throw new InvalidOperationException(
                    "Several verification codes arrived. Use the received code manually."
                );
            if (codes.Count == 1)
                return codes.Single();
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
    }
}
