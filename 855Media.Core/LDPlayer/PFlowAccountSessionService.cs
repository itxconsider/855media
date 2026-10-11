using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowWorkerIdentity(
    int DeviceIndex,
    string DeviceName,
    string InstanceIdentity,
    string AppPackage
);

public sealed record PFlowAccountObservation(
    string OwnerUid,
    string AppPackage,
    bool IsPersonal,
    bool OwnProfileControls,
    bool IsLiveUid,
    DateTimeOffset ObservedUtc,
    PFlowAuthenticationState State
);

public interface IPFlowAccountSessionNavigator
{
    Task<PFlowWorkerIdentity> ReadWorkerAsync(PFlowAccountBinding binding, CancellationToken ct);
    Task<PFlowAccountObservation> ObserveAsync(PFlowAccountBinding binding, CancellationToken ct);

    // Saved PERSONAL login switcher only. Managed Page dropdown and public profile links
    // cannot satisfy this contract. Unsupported layouts must return false, never guessed taps.
    Task<bool> SwitchSavedPersonalAccountAsync(PFlowAccountBinding binding, CancellationToken ct);
}

public interface IPFlowAccountSessionService
{
    Task<PFlowSessionResult> PreparePersonalAsync(
        PFlowPersonalAccount account,
        CancellationToken ct,
        bool deviceLeaseAlreadyHeld = false
    );

    Task<bool> EnsurePersonalAsync(
        PFlowPersonalAccount account,
        CancellationToken ct,
        bool deviceLeaseAlreadyHeld = false
    );
}

public sealed class PFlowAccountSessionService(IPFlowAccountSessionNavigator navigator)
    : IPFlowAccountSessionService
{
    public async Task<PFlowSessionResult> PreparePersonalAsync(
        PFlowPersonalAccount account,
        CancellationToken ct,
        bool deviceLeaseAlreadyHeld = false
    )
    {
        var binding = account.Binding;
        var attempt = Guid.NewGuid().ToString("N");
        try
        {
            var ready = await EnsurePersonalAsync(account, ct, deviceLeaseAlreadyHeld);
            var outcome =
                account.Binding != binding ? PFlowSessionOutcome.BindingMismatch
                : ready ? PFlowSessionOutcome.Ready
                : account.Authentication switch
                {
                    PFlowAuthenticationState.LoginRequired => PFlowSessionOutcome.LoginRequired,
                    PFlowAuthenticationState.Checkpoint => PFlowSessionOutcome.Checkpoint,
                    PFlowAuthenticationState.Unavailable => PFlowSessionOutcome.UnsupportedScreen,
                    _ => PFlowSessionOutcome.VerificationFailed,
                };
            return new(binding, attempt, outcome);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(binding, attempt, PFlowSessionOutcome.Cancelled);
        }
        catch (NotSupportedException)
        {
            return new(binding, attempt, PFlowSessionOutcome.UnsupportedScreen);
        }
        catch (InvalidOperationException)
        {
            return new(
                binding,
                attempt,
                account.Binding != binding
                    ? PFlowSessionOutcome.BindingMismatch
                    : PFlowSessionOutcome.VerificationFailed
            );
        }
    }

    public async Task<bool> EnsurePersonalAsync(
        PFlowPersonalAccount account,
        CancellationToken ct,
        bool deviceLeaseAlreadyHeld = false
    )
    {
        var binding = account.Binding;
        // Invalidate evidence before any cancellable wait or external navigation.
        account.Authentication = PFlowAuthenticationState.Cached;
        foreach (var page in account.Pages)
            page.VerifiedRunId = "";
        binding.Validate();
        using var lease = deviceLeaseAlreadyHeld
            ? null
            : await PFlowDeviceLease.AcquireAsync(binding.DeviceIndex, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        account.Authentication = PFlowAuthenticationState.Verifying;
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            await RequireWorkerAsync(binding, deadline.Token);
            var observed = await navigator.ObserveAsync(binding, deadline.Token);
            if (account.Binding != binding)
            {
                account.Authentication = PFlowAuthenticationState.VerificationFailed;
                return false;
            }
            if (NeedsUser(observed.State))
            {
                account.Authentication = observed.State;
                return false;
            }
            if (account.Binding != binding || !Verified(observed, binding))
            {
                if (!await navigator.SwitchSavedPersonalAccountAsync(binding, deadline.Token))
                {
                    account.Authentication = PFlowAuthenticationState.VerificationFailed;
                    return false;
                }
                // Exactly one navigation attempt. A new observation is required afterwards.
                await RequireWorkerAsync(binding, deadline.Token);
                observed = await navigator.ObserveAsync(binding, deadline.Token);
            }
            if (NeedsUser(observed.State))
            {
                account.Authentication = observed.State;
                return false;
            }
            if (account.Binding != binding || !Verified(observed, binding))
            {
                account.Authentication = PFlowAuthenticationState.VerificationFailed;
                return false;
            }
            await RequireWorkerAsync(binding, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            if (account.Binding != binding || !Verified(observed, binding))
            {
                account.Authentication = PFlowAuthenticationState.VerificationFailed;
                return false;
            }
            account.Authentication = PFlowAuthenticationState.Authenticated;
            account.LastVerifiedUtc = observed.ObservedUtc;
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            account.Authentication = PFlowAuthenticationState.VerificationFailed;
            return false;
        }
        catch
        {
            account.Authentication = PFlowAuthenticationState.VerificationFailed;
            throw;
        }
    }

    private async Task RequireWorkerAsync(PFlowAccountBinding binding, CancellationToken ct)
    {
        var worker = await navigator.ReadWorkerAsync(binding, ct);
        binding.RequireWorker(
            worker.DeviceIndex,
            worker.DeviceName,
            worker.InstanceIdentity,
            worker.AppPackage
        );
    }

    private static bool NeedsUser(PFlowAuthenticationState state) =>
        state
            is PFlowAuthenticationState.LoginRequired
                or PFlowAuthenticationState.Checkpoint
                or PFlowAuthenticationState.Unavailable;

    private static bool Verified(PFlowAccountObservation observed, PFlowAccountBinding binding) =>
        observed.OwnerUid == binding.OwnerUid
        && observed.AppPackage == binding.AppPackage
        && observed.State == PFlowAuthenticationState.Authenticated
        && observed.IsLiveUid
        && observed.IsPersonal
        && observed.OwnProfileControls
        && observed.ObservedUtc <= DateTimeOffset.UtcNow
        && observed.ObservedUtc >= DateTimeOffset.UtcNow.AddSeconds(-10);
}

public enum PFlowSessionOutcome
{
    VerificationFailed,
    Ready,
    LoginRequired,
    OtpRequired,
    DeviceApprovalRequired,
    CredentialsRejected,
    Checkpoint,
    UnsupportedScreen,
    BindingMismatch,
    NetworkUnavailable,
    Cancelled,
}

public sealed record PFlowSessionResult(
    PFlowAccountBinding Binding,
    string AttemptId,
    PFlowSessionOutcome Outcome
)
{
    public bool IsReady => Outcome == PFlowSessionOutcome.Ready;
}
