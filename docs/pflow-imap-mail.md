# IMAP verification mail implementation

11 October 2026 — implemented in C:/Users/itxco/.codex/worktrees/baf0/855Media and D:/repos/855Media.

## Scope and use
Add personal account -> paste UID|Password with optional 2FA/email -> expand Email code / IMAP mailbox -> enter host (default imap.yandex.com), TLS port (default 993), mailbox email and separate mail app password -> Test and save mailbox -> Start login -> when Facebook exposes a supported email challenge, Receive code from saved mailbox -> Use received code.
The mailbox is mapped to the exact UID + immutable LD/instance identity + app binding. Windows DPAPI CurrentUser protects the entire configuration with binding-specific entropy. No mailbox passwords or codes are written to normal JSON, logs or history. UI password clears after connection testing; code submission uses the existing pending-challenge inbox and native adapter that rechecks the challenge before entry. No checkpoint bypass or alternate LD routing is added.

## Behavior
- MailKit 4.17.0; TLS-on-connect only, normal certificate validation, read-only INBOX access. No mark-read, delete, move or send operations.
- Connection test capped at 30 seconds. Receive wait capped at two minutes; Stop email wait, Stop login, OTP cleanup and dialog close cancel pending work.
- Search recent received mail and inspect at most the newest 30 results, only InternalDate after the current login/challenge lower bound. Reject future/oversized messages; message body maximum 256KiB.
- Accept only exact configured recipient and security@facebookmail.com or noreply@facebookmail.com senders, recognized English login/security/confirmation/verification-code wording and exactly one distinct 6/8-digit value. Multiple matching codes are rejected, allowing manual entry. Unsupported/localized templates fall back to manual entry.
- Preview in the existing masked Received verification code field; explicit Use received code required. Does not automatically poll unsolicited email or submit on receipt.
- Yandex/compatible providers with supported IMAP app-password authentication. Gmail/Outlook OAuth connectors remain future work. A mailbox should be dedicated to its mapped Facebook account to avoid ambiguous concurrent codes.

## Changed files
- Directory.Packages.props; 855Media.Core/855Media.Core.csproj
- 855Media.Core/LDPlayer/PFlowMailService.cs (service, encrypted binding store, extractor)
- 855Media.Core.Tests/LDPlayer/PFlowMailTests.cs
- 855Media/ViewModels/Components/AutoPostViewModel.Mail.cs
- 855Media/ViewModels/Components/AutoPostViewModel.AddAccount.cs
- 855Media/ViewModels/Components/AutoPostViewModel.Otp.cs
- 855Media/Views/Components/AutoPostView.axaml

## Verification and limits
Focused mail/OTP/AddAccount tests: 61 passed in each checkout. Both Avalonia builds: zero warnings/errors, outputs C:/Users/itxco/AppData/Local/Temp/855Media-navigation-worktree and .../855Media-navigation-runtime. New checks cover strict sender/recipient, ambiguous code rejection, HTML/eight-digit extraction, invalid settings, encrypted persistence and binding isolation, and rejection of non-email/wrong-binding challenges.
No real mailbox login, email access, credentials or live social action was used in verification. UI compiled but was not visually inspected. Running app was not replaced. Load the updated build, enable provider IMAP/app-password settings and use Test and save mailbox to validate the connection. Exact Facebook challenge and personal UID guards remain in effect.

Provider setup: https://www.yandex.com/support/yandex-360/customers/mail/en/mail-clients/others
MailKit: https://www.nuget.org/packages/MailKit/4.17.0
