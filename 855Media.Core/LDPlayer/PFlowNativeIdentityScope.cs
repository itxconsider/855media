using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Memory-only actor proof: Graph/session for existing consumers, or explicit
// database-name/UI policy for posting. Never restored from persisted history.
public sealed class PFlowNativeIdentityScope
{
    private readonly PFlowAccountBinding _binding;
    private readonly string _process;
    private readonly string _fingerprint;
    private readonly FacebookPageTarget _target;
    public bool UsesDatabaseName { get; private init; }
    private int _invalidated;
    private readonly long _uiReaderEpoch;

    private PFlowNativeIdentityScope(
        PFlowAccountBinding binding,
        string process,
        string fingerprint,
        FacebookPageTarget target
    )
    {
        _binding = binding;
        _uiReaderEpoch = PFlowUiReaderEpoch.Read(binding.InstanceIdentity);
        _process = PFlowProcessIdentity.Normalize(process) ?? process;
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
            || PFlowProcessIdentity.Normalize(process) == null
            || (
                target.IsPrimaryProfile
                    ? identity.Id != target.UserUid
                    : identity.Id != target.PageId && identity.Id != target.PersonaUid
            )
        )
            return null;
        return new(binding, process, identity.AuthenticationFingerprint, target);
    }

    internal static PFlowNativeIdentityScope? FromSelectedName(
        PFlowAccountBinding binding,
        string process,
        FacebookPageTarget target,
        PFlowPostingNameVerification.Actor? selected,
        System.Collections.Generic.IEnumerable<FacebookPageTarget> savedTargets
    )
    {
        if (
            selected == null
            || selected.Type != target.Type
            || !PFlowNameMatch.Same(selected.Name, target.PageName)
            || target.Type is not ("Profile" or "Page")
            || target.IsPrimaryProfile && target.PageId != target.UserUid
            || binding.AppPackage != "com.facebook.katana"
            || target.UserUid != binding.OwnerUid
            || target.AccountBinding != binding
            || PFlowProcessIdentity.Normalize(process) == null
            || !PFlowPostingNameVerification.MatchesSavedTarget(target, savedTargets)
        )
            return null;
        return new(binding, process, "", target) { UsesDatabaseName = true };
    }

    // Posting-only proof from a complete typed switcher; stored IDs only route history.
    public static PFlowNativeIdentityScope? FromVerifiedName(
        PFlowAccountBinding binding,
        string process,
        FacebookPageTarget target,
        string switcherXml,
        System.Collections.Generic.IEnumerable<FacebookPageTarget> savedTargets
    )
    {
        var selected = PFlowPersonaSwitcherService
            .InferNativeSwitcherActors(switcherXml)
            .Where(a => a.IsSelected)
            .Take(2)
            .ToArray();
        return FromSelectedName(
            binding,
            process,
            target,
            selected.Length == 1 ? new(selected[0].Name, selected[0].Type) : null,
            savedTargets
        );
    }

    public bool Matches(PFlowAccountBinding binding, string process, FacebookPageTarget target) =>
        Volatile.Read(ref _invalidated) == 0
        && _uiReaderEpoch == PFlowUiReaderEpoch.Read(_binding.InstanceIdentity)
        && _binding == binding
        && PFlowProcessIdentity.Same(_process, process)
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
    ) =>
        _uiReaderEpoch == PFlowUiReaderEpoch.Read(_binding.InstanceIdentity)
        && (
            UsesDatabaseName
                ? await ProcessUnchangedAsync(_binding, _process, ld, path, ct)
                : await ReadFingerprintAsync(_binding, _process, ld, path, ct) == _fingerprint
        )
        && _uiReaderEpoch == PFlowUiReaderEpoch.Read(_binding.InstanceIdentity);

    // Read-only monitoring of an already submitted job. Reader recovery invalidates
    // future actor/input proof, but does not itself change this submission's process.
    internal async Task<bool> ObserveSubmittedSessionAsync(
        PFlowJob job,
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    )
    {
        bool Snapshot() =>
            Volatile.Read(ref _invalidated) == 0
            && UsesDatabaseName
            && job.State == PFlowState.Submitted
            && job.SubmittedUtc.HasValue
            && job.AccountBinding == _binding
            && job.DeviceIndex == _binding.DeviceIndex
            && job.AppPackage == _binding.AppPackage
            && job.TargetId == _target.PageId
            && job.OwnerUid == _target.UserUid
            && job.PersonaUid == _target.PersonaUid
            && job.TargetName == _target.PageName
            && job.IsProfile == _target.IsPrimaryProfile;
        return Snapshot()
            && await ProcessUnchangedAsync(_binding, _process, ld, path, ct)
            && Snapshot();
    }

    private static async Task<bool> ProcessUnchangedAsync(
        PFlowAccountBinding binding,
        string process,
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    )
    {
        var activity = await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
        if (
            activity != binding.AppPackage
            && !activity.StartsWith(binding.AppPackage + "/", StringComparison.Ordinal)
        )
            return false;
        return PFlowProcessIdentity.Same(
            (
                await ld.ExecuteAdbAsync(
                    binding.DeviceIndex,
                    "shell pidof com.facebook.katana",
                    path,
                    ct
                ) ?? ""
            ).Trim(),
            process
        );
    }

    private static Task<string?> ReadFingerprintAsync(
        PFlowAccountBinding binding,
        string process,
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        // A legacy Graph proof cannot be refreshed without an authorized credential source.
        // Never substitute a PID, saved UID or UI name for its old authenticated fingerprint.
        return Task.FromResult<string?>(null);
    }

    // Consecutive confirmed posts reuse the actor proof without navigating its profile.
    // A pending label may be observed while waiting, but never authorizes posting.
    public async Task<bool> VerifyContinuationWaitAsync(
        PFlowJob job,
        string xml,
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    ) =>
        Matches(job)
        && PFlowPostTargetSession.CanContinueContext(xml, job.AppPackage)
        && !(
            UsesDatabaseName
                ? PFlowPostingNameVerification.Contradicts(xml, _target)
                : PFlowSessionProfileUi.Contradicts(xml, _target)
        )
        && await SessionUnchangedAsync(ld, path, ct)
        && Matches(job);

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
                && PFlowPostTargetSession.CanContinueForJob(job, xml)
                && !(
                    UsesDatabaseName
                        ? PFlowPostingNameVerification.Contradicts(xml, _target)
                        : PFlowSessionProfileUi.Contradicts(xml, _target)
                )
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
        if (UsesDatabaseName)
        {
            var xml = await ld.DumpUiHierarchyAsync(_binding.DeviceIndex, path, ct) ?? "";
            return PFlowPostTargetSession.CanContinueFrom(xml, _binding.AppPackage)
                && !PFlowPostingNameVerification.Contradicts(xml, _target);
        }
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
