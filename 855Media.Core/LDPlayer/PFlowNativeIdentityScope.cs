using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Memory-only handoff from a live Graph check. Native session bytes never authorize
// work alone: every repeat check must also confirm the current actor's own profile UI.
public sealed class PFlowNativeIdentityScope
{
    private readonly PFlowAccountBinding _binding;
    private readonly string _process;
    private readonly string _fingerprint;
    private readonly FacebookPageTarget _target;
    private int _invalidated;

    private PFlowNativeIdentityScope(
        PFlowAccountBinding binding,
        string process,
        string fingerprint,
        FacebookPageTarget target
    )
    {
        _binding = binding;
        _process = process;
        _fingerprint = fingerprint;
        _target = new()
        {
            PageId = target.PageId,
            PersonaUid = target.PersonaUid,
            UserUid = target.UserUid,
            PageName = target.PageName,
            Type = target.Type,
        };
    }

    public static PFlowNativeIdentityScope? FromVerifiedGraph(
        PFlowAccountBinding binding,
        string process,
        FacebookPageTarget target,
        PFlowGraphIdentity identity
    )
    {
        if (
            identity.AuthenticationFingerprint.Length == 0
            || binding.AppPackage != "com.facebook.katana"
            || !Numeric(identity.Id)
            || !Numeric(target.PageId)
            || !Numeric(target.UserUid)
            || !PFlowSessionProfileUi.SameName(identity.Name, target.PageName)
            || identity.UnderlyingUid != binding.OwnerUid
            || target.UserUid != binding.OwnerUid
            || target.AccountBinding != null && target.AccountBinding != binding
            || string.IsNullOrWhiteSpace(process)
            || !process
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(p => p.All(char.IsAsciiDigit))
            || (
                target.IsPrimaryProfile
                    ? identity.Id != target.UserUid
                    : identity.Id != target.PageId && identity.Id != target.PersonaUid
            )
        )
            return null;
        return new(binding, process, identity.AuthenticationFingerprint, target);
    }

    public bool Matches(PFlowAccountBinding binding, string process, FacebookPageTarget target) =>
        Volatile.Read(ref _invalidated) == 0
        && _binding == binding
        && _process == process
        && _target.PageId == target.PageId
        && _target.UserUid == target.UserUid
        && _target.PersonaUid == target.PersonaUid
        && _target.PageName == target.PageName
        && _target.IsPrimaryProfile == target.IsPrimaryProfile;

    public bool Matches(PFlowJob job) =>
        job.AccountBinding != null
        && Matches(
            job.AccountBinding,
            _process,
            new FacebookPageTarget
            {
                PageId = job.TargetId,
                UserUid = job.OwnerUid,
                PersonaUid = job.PersonaUid,
                PageName = job.TargetName,
                Type = job.IsProfile ? "Profile" : "Page",
            }
        )
        && job.DeviceIndex == _binding.DeviceIndex
        && job.AppPackage == _binding.AppPackage;

    public async Task<bool> SessionUnchangedAsync(
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    ) => await ReadFingerprintAsync(_binding, _process, ld, path, ct) == _fingerprint;

    private static async Task<string?> ReadFingerprintAsync(
        PFlowAccountBinding binding,
        string process,
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    )
    {
        if (binding.AppPackage != "com.facebook.katana")
            return null;
        var activity = await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
        if (
            activity != binding.AppPackage
            && !activity.StartsWith(binding.AppPackage + "/", StringComparison.Ordinal)
        )
            return null;
        var pid = (
            await ld.ExecuteAdbAsync(
                binding.DeviceIndex,
                "shell pidof com.facebook.katana",
                path,
                ct
            ) ?? ""
        ).Trim();
        if (pid != process)
            return null;
        const string prefix =
            "shell su 0 cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/";
        var auth =
            await ld.ExecuteAdbAsync(binding.DeviceIndex, prefix + "authentication", path, ct)
            ?? "";
        var token = PFlowGraphIdentityReader.Token(auth);
        if (token.Length == 0)
            return null;
        var ownerRaw =
            await ld.ExecuteAdbAsync(binding.DeviceIndex, prefix + "underlying_account", path, ct)
            ?? "";
        var owner = PFlowGraphIdentityReader.Field(
            PFlowGraphIdentityReader.Clean(ownerRaw),
            "underlying_account_uid"
        );
        if (!Numeric(owner))
            owner = PFlowGraphIdentityReader.Field(
                PFlowGraphIdentityReader.Clean(auth),
                "underlying_account_uid"
            );
        if (!Numeric(owner))
            owner = PFlowGraphIdentityReader.Field(PFlowGraphIdentityReader.Clean(auth), "user_id");
        return owner == binding.OwnerUid
            ? PFlowGraphIdentityReader.Fingerprint(token, owner)
            : null;
    }

    // Consecutive confirmed posts reuse the actor proof without navigating its profile.
    public async Task<bool> VerifyContinuationAsync(
        PFlowJob job,
        string xml,
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (
                Matches(job)
                && PFlowPostTargetSession.CanContinueFrom(xml, job.AppPackage)
                && !PFlowSessionProfileUi.Contradicts(xml, _target)
                && await SessionUnchangedAsync(ld, path, ct)
                && Matches(job)
            )
                return true;
            Interlocked.Exchange(ref _invalidated, 1);
            return false;
        }
        catch
        {
            Interlocked.Exchange(ref _invalidated, 1);
            throw;
        }
    }

    public async Task<bool> VerifyAsync(ILDPlayerService ld, string? path, CancellationToken ct) =>
        await VerifyAsync(ld, path, c => LookupCurrentTargetAsync(ld, path, c), ct);

    internal async Task<bool> VerifyAsync(
        ILDPlayerService ld,
        string? path,
        Func<CancellationToken, Task<bool>> nativeLookup,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (
                Volatile.Read(ref _invalidated) == 0
                && await SessionUnchangedAsync(ld, path, ct)
                && await nativeLookup(ct)
            )
            {
                ct.ThrowIfCancellationRequested();
                if (await SessionUnchangedAsync(ld, path, ct))
                    return true;
            }
            Interlocked.Exchange(ref _invalidated, 1);
            return false;
        }
        catch
        {
            Interlocked.Exchange(ref _invalidated, 1);
            throw;
        }
    }

    private async Task<bool> LookupCurrentTargetAsync(
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    )
    {
        var initial = await ld.DumpUiHierarchyAsync(_binding.DeviceIndex, path, ct) ?? "";
        var nodes = ScreenTimeHierarchy.Nodes(initial).ToArray();
        if (
            !nodes.Any(n => (string?)n.Attribute("package") == _binding.AppPackage)
            || nodes.Any(n =>
                !string.IsNullOrEmpty((string?)n.Attribute("package"))
                && (string?)n.Attribute("package") != _binding.AppPackage
                && (string?)n.Attribute("package") != "com.android.systemui"
            )
        )
            return false;
        if (
            !PFlowSessionProfileUi.Safe(initial)
            || PFlowSessionProfileUi.Contradicts(initial, _target)
        )
            return false;
        var own = _target.IsPrimaryProfile
            ? PFlowNativeAccountNavigator.OwnProfile(initial)
            : PFlowOwnProfileNavigation.IsOwnPageProfile(
                AtxAgentClient.ParseBloksHierarchy(initial),
                _target.PageName
            );
        if (
            !own
            && (
                FacebookLayoutRegistry.Match(initial, FacebookScreenKind.ReelsFeed).IsMatch
                || nodes.Any(n =>
                    (string?)n.Attribute("package") == _binding.AppPackage
                    && (
                        (string?)n.Attribute("text") == "Reel details"
                        || (string?)n.Attribute("content-desc") == "Reel details"
                    )
                )
            )
        )
        {
            var back = ScreenTimeHierarchy.Control(initial, "Back");
            var point =
                back == null ? null : FacebookLayoutRegistry.UsableCenter(back, readOnlyList: true);
            if (
                point == null
                || !await ld.TapAsync(_binding.DeviceIndex, point.Value.X, point.Value.Y, path, ct)
            )
                return false;
            await Task.Delay(300, ct);
        }
        if (
            !await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                _binding.DeviceIndex,
                _target.PageName,
                path,
                screen => PFlowSessionProfileUi.Confirms(screen.RawXml ?? "", _target),
                ct,
                navigateCurrentActor: true,
                canNavigate: xml =>
                    PFlowSessionProfileUi.Safe(xml)
                    && !PFlowSessionProfileUi.Contradicts(xml, _target)
            )
        )
            return false;
        var fresh = await ld.DumpUiHierarchyAsync(_binding.DeviceIndex, path, ct) ?? "";
        return PFlowSessionProfileUi.Confirms(fresh, _target);
    }

    private static bool Numeric(string value) =>
        value.Length is >= 10 and <= 20 && value.All(char.IsAsciiDigit);
}
