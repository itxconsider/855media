using System;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed class PFlowAddAccountInput : IDisposable
{
    public PFlowAccountBinding Binding { get; }
    public DateOnly AccountDate { get; }
    public string Email { get; }
    public PFlowCredential Credential { get; }

    private PFlowAddAccountInput(
        PFlowAccountBinding binding,
        DateOnly date,
        string email,
        PFlowCredential credential
    ) => (Binding, AccountDate, Email, Credential) = (binding, date, email, credential);

    public static PFlowAddAccountInput Parse(
        string line,
        int index,
        string name,
        string identity,
        string package
    )
    {
        if (line.Length > 65536 || line.Contains('\n') || line.Contains('\r'))
            throw new FormatException("Paste one account line without line breaks.");
        var parts = line.Split('|');
        if (parts.Length < 2 || parts.Length > 5)
            throw new FormatException(
                "Expected UID|Password, optionally followed by |2FA secret|Email."
            );
        var binding = new PFlowAccountBinding(identity, index, name, package, parts[0].Trim());
        binding.Validate();
        var date = default(DateOnly);
        // Accept the previous five-field import for saved user workflows only.
        if (
            parts.Length == 5
            && !DateOnly.TryParseExact(
                parts[3].Trim(),
                ["d/M/yyyy", "dd/MM/yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date
            )
        )
            throw new FormatException("Use a date in dd/MM/yyyy format.");
        var email = parts.Length >= 4 ? parts[^1].Trim() : string.Empty;
        if (
            email.Length > 0
            && (!MailAddress.TryCreate(email, out var address) || address.Address != email)
        )
            throw new FormatException("Enter a valid email address in the last field.");
        var factor = parts.Length >= 3 ? parts[2] : string.Empty;
        var kind = string.IsNullOrWhiteSpace(factor)
            ? PFlowTwoFactorKind.None
            : PFlowTwoFactorKind.TotpSecret;
        var credential = PFlowCredentialImport.Parse(
            string.Join('|', parts[0], parts[1], factor, ""),
            binding,
            binding.OwnerUid,
            kind
        );
        return new(binding, date, email, credential);
    }

    public void Dispose() => Credential.Dispose();

    public override string ToString() => "Account login input (credentials hidden)";
}

public interface IPFlowLoginNavigator
{
    Task<PFlowWorkerIdentity> ReadWorkerAsync(PFlowAccountBinding binding, CancellationToken ct);
    Task<PFlowAccountObservation> LoginAsync(
        PFlowAccountBinding binding,
        PFlowCredential credential,
        Func<string, Task> progress,
        CancellationToken ct
    );
}

// Credentials are committed only after exact, fresh personal-actor evidence on the assigned instance.
public sealed class PFlowAddAccountService(
    PFlowAccountStore accounts,
    IPFlowCredentialStore credentials,
    IPFlowLoginNavigator navigator
)
{
    private static void RequireAccountLock(
        System.Collections.Generic.List<PFlowPersonalAccount> saved,
        PFlowAccountBinding binding
    )
    {
        if (saved.Any(a => a.Binding.OwnerUid == binding.OwnerUid && a.Binding != binding))
            throw new InvalidOperationException(
                "This UID is already locked to a different LD or app binding. Use its assigned instance."
            );
    }

    public async Task<PFlowPersonalAccount> AddAsync(
        PFlowAddAccountInput input,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        using var lease = await PFlowDeviceLease.AcquireAsync(input.Binding.DeviceIndex, token);
        // Lock and validate before any login input reaches Android.
        await accounts.UpdateAsync(saved => RequireAccountLock(saved, input.Binding), token);
        async Task RequireWorker()
        {
            var worker = await navigator.ReadWorkerAsync(input.Binding, token);
            input.Binding.RequireWorker(
                worker.DeviceIndex,
                worker.DeviceName,
                worker.InstanceIdentity,
                worker.AppPackage
            );
        }
        await RequireWorker();
        var observed = await navigator.LoginAsync(input.Binding, input.Credential, progress, token);
        await RequireWorker();
        var now = DateTimeOffset.UtcNow;
        if (
            observed.State != PFlowAuthenticationState.Authenticated
            || !observed.IsPersonal
            || !observed.OwnProfileControls
            || !observed.IsLiveUid
            || observed.OwnerUid != input.Binding.OwnerUid
            || observed.AppPackage != input.Binding.AppPackage
            || observed.ObservedUtc > now
            || observed.ObservedUtc < now.AddSeconds(-10)
        )
            throw new InvalidOperationException(
                "Login identity was not confirmed as the requested personal UID. Credentials were not saved."
            );
        token.ThrowIfCancellationRequested();
        await progress("Personal UID confirmed. Saving protected credentials and its LD binding.");
        string? reference = null;
        string previous = "";
        bool committed = false;
        PFlowPersonalAccount? result = null;
        try
        {
            reference = await credentials.SaveAsync(input.Binding, input.Credential, token);
            await RequireWorker();
            await accounts.UpdateAsync(
                saved =>
                {
                    RequireAccountLock(saved, input.Binding);
                    result = saved.SingleOrDefault(a => a.Binding == input.Binding);
                    if (result == null)
                    {
                        result = new PFlowPersonalAccount { Binding = input.Binding };
                        saved.Add(result);
                    }
                    previous = result.CredentialReference;
                    result.CredentialReference = reference;
                    result.Email = input.Email;
                    result.InstanceLocked = true;
                    if (input.AccountDate != default)
                        result.AccountDate = input.AccountDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture
                        );
                    result.Authentication = PFlowAuthenticationState.Authenticated;
                    result.LastVerifiedUtc = observed.ObservedUtc;
                    // Existing run choices survive; a new account starts with all actions opt-in.
                },
                token
            );
            committed = true;
            return result!;
        }
        finally
        {
            try
            {
                if (!committed && reference != null)
                    credentials.Delete(reference);
                if (committed && previous.Length > 0 && previous != reference)
                    credentials.Delete(previous);
            }
            catch
            { /* Protected orphan cannot be used without its authoritative binding/reference. */
            }
        }
    }
}
