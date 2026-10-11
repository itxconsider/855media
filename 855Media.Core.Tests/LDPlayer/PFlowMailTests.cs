using _855Media.Core.LDPlayer;
using MimeKit;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowMailTests
{
    private static MimeMessage Message(
        string content,
        string sender = "security@facebookmail.com",
        string recipient = "fixture@yandex.com"
    )
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(sender));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "Facebook security code";
        message.Body = new TextPart("plain") { Text = content };
        return message;
    }

    [Fact]
    public void ReadsSingleCodeAndRejectsAmbiguity()
    {
        Assert.Equal(
            "123456",
            PFlowMailService.ExtractCode(
                Message("Your login code is 123456"),
                "fixture@yandex.com",
                6
            )
        );
        Assert.Null(
            PFlowMailService.ExtractCode(Message("123456 or 654321"), "fixture@yandex.com", 6)
        );
        Assert.Null(PFlowMailService.ExtractCode(Message("123456"), "other@yandex.com", 6));
        Assert.Null(
            PFlowMailService.ExtractCode(
                Message("123456", "security@facebookmail.com.attacker.test"),
                "fixture@yandex.com",
                6
            )
        );
    }

    [Fact]
    public void ReadsHtmlAndEightDigitCodeWithoutExposingPassword()
    {
        var message = Message("");
        message.Body = new TextPart("html")
        {
            Text = "<p>Your verification code is <b>12345678</b></p>",
        };
        Assert.Equal("12345678", PFlowMailService.ExtractCode(message, "fixture@yandex.com", 8));
        Assert.Null(PFlowMailService.ExtractCode(message, "fixture@yandex.com", 6));
        var mailbox = new PFlowMailbox(
            "imap.yandex.com",
            993,
            "fixture@yandex.com",
            "fixture-app-password"
        );
        Assert.DoesNotContain(mailbox.AppPassword, mailbox.ToString());
    }

    [Theory]
    [InlineData("https://imap.yandex.com", 993, "fixture@yandex.com", "password")]
    [InlineData("imap.yandex.com", 0, "fixture@yandex.com", "password")]
    [InlineData("imap.yandex.com", 993, "invalid", "password")]
    [InlineData("imap.yandex.com", 993, "fixture@yandex.com", "")]
    public void ValidatesConnection(string host, int port, string email, string password) =>
        Assert.Throws<FormatException>(() =>
            new PFlowMailbox(host, port, email, password).Validate()
        );

    [Fact]
    public async Task ProtectedMailboxIsBoundToAccountAndInstance()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "pflow-mail-test-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var store = new PFlowMailboxStore(directory);
            var binding = new PFlowAccountBinding(
                "fixture",
                900074,
                "fixture-LD",
                "com.facebook.katana",
                "123"
            );
            var mailbox = new PFlowMailbox(
                "imap.yandex.com",
                993,
                "fixture@yandex.com",
                "fixture-app-password"
            );
            await store.SaveAsync(binding, mailbox, default);
            Assert.Equal(mailbox, await store.LoadAsync(binding, default));
            var bytes = await File.ReadAllBytesAsync(Directory.GetFiles(directory).Single());
            Assert.DoesNotContain(mailbox.AppPassword, System.Text.Encoding.UTF8.GetString(bytes));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.LoadAsync(binding with { DeviceIndex = 900075 }, default)
            );
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task NonEmailOrWrongBindingCannotConnect()
    {
        var binding = new PFlowAccountBinding(
            "fixture",
            900074,
            "fixture-LD",
            "com.facebook.katana",
            "123"
        );
        var now = DateTimeOffset.UtcNow;
        var request = new PFlowOtpRequest(binding, "fixture-challenge", PFlowOtpMethod.Sms, 6, now);
        var mailbox = new PFlowMailbox(
            "imap.yandex.com",
            993,
            "fixture@yandex.com",
            "fixture-app-password"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowMailService().WaitForCodeAsync(
                mailbox,
                request,
                binding,
                now.AddSeconds(-1),
                default
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowMailService().WaitForCodeAsync(
                mailbox,
                request with
                {
                    Method = PFlowOtpMethod.Email,
                },
                binding with
                {
                    OwnerUid = "456",
                },
                now.AddSeconds(-1),
                default
            )
        );
    }
}
