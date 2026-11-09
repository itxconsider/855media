using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Invocation-local personal actor proof. Stored IDs identify history/mappings;
// they are not represented as freshly observed Facebook UIDs.
public sealed class ScreenTimeNameSession(
    ILDPlayerService ld,
    PFlowAccountBinding binding,
    string accountName,
    FacebookPageTarget profile,
    string? path,
    Func<CancellationToken, Task>? requireRecoveryIdle = null
)
{
    private readonly FacebookPageTarget _expected = profile.CreateSnapshot();
    private PFlowNativeIdentityScope? _scope;
    private bool _failed;

    public async Task<bool> VerifyAsync(
        FacebookPageTarget current,
        IEnumerable<FacebookPageTarget> savedTargets,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            binding.Validate();
            var saved = savedTargets.Select(t => t.CreateSnapshot()).ToArray();
            current = current.CreateSnapshot();
            if (
                _failed
                || binding.AppPackage != "com.facebook.katana"
                || _expected.Type != "Profile"
                || !current.IsPrimaryProfile
                || current.Type != "Profile"
                || current.AccountBinding != binding
                || current.UserUid != binding.OwnerUid
                || current.PageId != binding.OwnerUid
                || !string.IsNullOrEmpty(current.PersonaUid)
                    && current.PersonaUid != binding.OwnerUid
                || !PFlowNameMatch.Same(current.PageName, accountName)
                || current.PageId != _expected.PageId
                || current.PersonaUid != _expected.PersonaUid
                || current.UserUid != _expected.UserUid
                || current.AccountBinding != _expected.AccountBinding
                || !PFlowNameMatch.Same(current.PageName, _expected.PageName)
                || saved.Count(t => t.IsPrimaryProfile) != 1
                || !PFlowPostingNameVerification.MatchesSavedTarget(current, saved)
            )
                throw new InvalidOperationException(
                    "ScreenTime needs one unambiguous saved personal account name and matching LD mapping."
                );

            var xml = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
            if (PFlowConfirmation.HasPendingPublication(xml))
                throw new InvalidOperationException(
                    "ScreenTime cannot change or verify actors while Facebook publication is pending."
                );
            if (_scope == null)
                _scope = await PFlowPostingNameVerification.ActivateAsync(
                    ld,
                    binding,
                    current,
                    saved,
                    path,
                    ct,
                    requireRecoveryIdle
                );
            if (!await _scope.VerifyAsync(ld, path, ct))
                throw new InvalidOperationException(
                    "ScreenTime personal actor session changed or Facebook became unsafe. Start a freshly verified session."
                );

            await PFlowTrace.SessionAsync(
                binding.DeviceIndex,
                "ScreenTime.identity.database-name",
                "Saved personal name/type and native session verified; Graph calls=0; live UID lookup=0.",
                ct
            );
            return true;
        }
        catch
        {
            _failed = true;
            _scope = null;
            throw;
        }
    }
}
