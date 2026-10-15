using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public interface IPFlowAutomaticAccountNavigator : IPFlowLoginNavigator
{
    Task<PFlowAccountObservation> ObserveAsync(PFlowAccountBinding binding, CancellationToken ct);

    // This opens a supported password form, logging out only when explicitly allowed.
    Task OpenLoginAsync(PFlowAccountBinding binding, bool allowLogout, CancellationToken ct);
}

// One instance per device batch. An uncertain password attempt cannot be repeated in that batch.
public sealed class PFlowAutomaticSessionService(
    IPFlowAutomaticAccountNavigator navigator,
    IPFlowCredentialStore credentials,
    Func<CancellationToken, Task> requireIdle
)
{
    private readonly HashSet<string> _loginAttempts = [];

    public static bool Verified(PFlowAccountObservation observed, PFlowAccountBinding binding) =>
        observed.OwnerUid == binding.OwnerUid
        && observed.AppPackage == binding.AppPackage
        && observed.State == PFlowAuthenticationState.Authenticated
        && observed.IsPersonal
        && observed.OwnProfileControls
        && observed.IsLiveUid
        && observed.ObservedUtc <= DateTimeOffset.UtcNow
        && observed.ObservedUtc >= DateTimeOffset.UtcNow.AddSeconds(-10);

    public async Task EnsureAsync(
        PFlowPersonalAccount account,
        Func<string, Task> progress,
        CancellationToken ct,
        bool deviceLeaseAlreadyHeld = false
    )
    {
        var binding = account.Binding;
        account.Authentication = PFlowAuthenticationState.Verifying;
        account.LastVerifiedUtc = null;
        foreach (var page in account.Pages)
            page.VerifiedRunId = "";
        binding.Validate();
        using var lease = deviceLeaseAlreadyHeld
            ? null
            : await PFlowDeviceLease.AcquireAsync(binding.DeviceIndex, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        async Task RequireBinding()
        {
            token.ThrowIfCancellationRequested();
            if (account.Binding != binding)
                throw new InvalidOperationException(
                    "Account mapping changed during authentication."
                );
            var worker = await navigator.ReadWorkerAsync(binding, token);
            binding.RequireWorker(
                worker.DeviceIndex,
                worker.DeviceName,
                worker.InstanceIdentity,
                worker.AppPackage
            );
        }
        try
        {
            await RequireBinding();
            await requireIdle(token);
            await progress("Verifying personal account " + binding.OwnerUid);
            var observed = await navigator.ObserveAsync(binding, token);
            await RequireBinding();
            if (!Verified(observed, binding))
            {
                account.Authentication = observed.State;
                if (observed.State == PFlowAuthenticationState.Checkpoint)
                    throw new InvalidOperationException(
                        "Facebook is showing an account verification challenge in the assigned LD."
                    );
                if (observed.State == PFlowAuthenticationState.Unavailable)
                    throw new InvalidOperationException(
                        "Personal-profile recovery could not confirm the live account UID after automatic recovery. Facebook was left open."
                    );
                var otherAccount =
                    !string.IsNullOrWhiteSpace(observed.OwnerUid)
                    && observed.OwnerUid.All(char.IsAsciiDigit)
                    && Verified(observed, binding with { OwnerUid = observed.OwnerUid })
                    && observed.OwnerUid != binding.OwnerUid;
                if (!otherAccount && observed.State != PFlowAuthenticationState.LoginRequired)
                    throw new InvalidOperationException(
                        "Current personal identity could not be verified; login was not attempted."
                    );
                if (otherAccount && !account.AllowAccountRotation)
                    throw new InvalidOperationException(
                        "Enable account rotation for this account before changing the active login."
                    );
                if (
                    !account.AllowAutomaticLogin
                    || string.IsNullOrWhiteSpace(account.CredentialReference)
                )
                    throw new InvalidOperationException(
                        "Store protected credentials and enable automatic login for this account."
                    );
                if (_loginAttempts.Contains(binding.AccountKey))
                    throw new InvalidOperationException(
                        "This account already had a login attempt in this batch. Review Facebook before retrying."
                    );
                // Verify credentials are readable before logging out the current account.
                using var credential = await credentials.LoadAsync(
                    binding,
                    account.CredentialReference,
                    token
                );
                await RequireBinding();
                await requireIdle(token);
                _loginAttempts.Add(binding.AccountKey);
                await progress(
                    otherAccount
                        ? "Changing personal login on the assigned LD"
                        : "Recovering expired personal login"
                );
                await navigator.OpenLoginAsync(
                    binding,
                    otherAccount && account.AllowAccountRotation,
                    token
                );
                await RequireBinding();
                await requireIdle(token);
                observed = await navigator.LoginAsync(binding, credential, progress, token);
                await RequireBinding();
                if (!Verified(observed, binding))
                    throw new InvalidOperationException(
                        "Login did not verify the requested live personal UID; its tasks were not started."
                    );
            }
            await RequireBinding();
            if (!Verified(observed, binding))
                throw new InvalidOperationException(
                    "Personal identity evidence expired before task execution."
                );
            account.Authentication = PFlowAuthenticationState.Authenticated;
            account.LastVerifiedUtc = observed.ObservedUtc;
            await progress("Personal account verified: " + binding.OwnerUid);
        }
        catch
        {
            if (
                account.Authentication
                is not (
                    PFlowAuthenticationState.Checkpoint
                    or PFlowAuthenticationState.LoginRequired
                    or PFlowAuthenticationState.Unavailable
                )
            )
                account.Authentication = PFlowAuthenticationState.VerificationFailed;
            throw;
        }
    }

    public static PFlowPersonalAccount[] Plan(
        IEnumerable<PFlowPersonalAccount> accounts,
        int index,
        string package,
        bool screenTime,
        bool posting
    )
    {
        var selected = accounts
            .Where(a =>
                a.Binding.DeviceIndex == index
                && a.Binding.AppPackage == package
                && (screenTime && a.SelectedForScreenTime || posting && a.SelectedForPosting)
            )
            .ToArray();
        foreach (var account in selected)
        {
            account.Binding.Validate();
            if (account.RotationOrder < 0)
                throw new InvalidOperationException("Account order must be zero or greater.");
        }
        if (selected.GroupBy(a => a.Binding.OwnerUid).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Duplicate selected personal account mapping.");
        return selected
            .OrderBy(a => a.RotationOrder)
            .ThenBy(a => a.Binding.OwnerUid, StringComparer.Ordinal)
            .ToArray();
    }
}
