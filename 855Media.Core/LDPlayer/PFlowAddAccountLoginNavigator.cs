using System;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Used by the explicit Add Account command, which already owns the device lease.
public sealed class PFlowAddAccountLoginNavigator(
    IPFlowAutomaticAccountNavigator navigator,
    Func<PFlowAccountBinding, Func<string, Task>, CancellationToken, Task> prepare,
    Func<CancellationToken, Task> requireIdle
) : IPFlowLoginNavigator
{
    public Task<PFlowWorkerIdentity> ReadWorkerAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    ) => navigator.ReadWorkerAsync(binding, ct);

    public async Task<PFlowAccountObservation> LoginAsync(
        PFlowAccountBinding binding,
        PFlowCredential credential,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        await requireIdle(ct);
        await prepare(binding, progress, ct);
        var observed = await navigator.ObserveAsync(binding, ct);
        if (PFlowAutomaticSessionService.Verified(observed, binding))
            return observed;
        var authenticated =
            !string.IsNullOrWhiteSpace(observed.OwnerUid)
            && PFlowAutomaticSessionService.Verified(
                observed,
                binding with
                {
                    OwnerUid = observed.OwnerUid,
                }
            );
        if (!authenticated && observed.State != PFlowAuthenticationState.LoginRequired)
            throw new InvalidOperationException(
                "Review the current Facebook session or verification challenge before adding an account."
            );
        await progress(
            authenticated
                ? "Logging out the current personal account to add the requested account."
                : "Opening the password login form."
        );
        await requireIdle(ct);
        await navigator.OpenLoginAsync(binding, authenticated, ct);
        return await navigator.LoginAsync(binding, credential, progress, ct);
    }
}
