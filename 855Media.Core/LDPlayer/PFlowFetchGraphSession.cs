using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Fetch-only, invocation-local identity proof. Never persisted or shared with posting.
public sealed class PFlowFetchGraphSession : IDisposable
{
    private static readonly AsyncLocal<PFlowFetchGraphSession?> Slot = new();
    internal static PFlowFetchGraphSession? Current =>
        Slot.Value is { _disposed: false } s ? s : null;
    private readonly PFlowFetchGraphSession? _previous;
    private readonly ILDPlayerService _ld;
    private readonly int _index;
    private readonly string? _path;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Dictionary<string, PFlowGraphActivationProof> _proofs = new();
    private string _process = "",
        _owner = "";
    private bool _disposed;

    public PFlowFetchGraphSession(ILDPlayerService ld, int index, string? path)
    {
        _ld = ld;
        _index = index;
        _path = path;
        _previous = Slot.Value;
        Slot.Value = this;
    }

    internal bool Applies(ILDPlayerService ld, int index, string? path) =>
        !_disposed && ReferenceEquals(ld, _ld) && index == _index && path == _path;

    internal async Task<PFlowGraphIdentity?> ReadAsync(
        Func<Task<PFlowGraphIdentity?>> fresh,
        CancellationToken ct
    )
    {
        await _gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (_disposed)
                throw new ObjectDisposedException(nameof(PFlowFetchGraphSession));
            var pid = (
                await _ld.ExecuteAdbAsync(_index, "shell pidof com.facebook.katana", _path, ct)
                ?? ""
            ).Trim();
            var activity = await _ld.GetCurrentFocusedActivityAsync(_index, _path, ct) ?? "";
            if (
                pid.Length == 0
                || (
                    activity != "com.facebook.katana"
                    && !activity.StartsWith("com.facebook.katana/", StringComparison.Ordinal)
                )
            )
            {
                _proofs.Clear();
                return null;
            }
            const string prefix =
                "shell su 0 cat /data/data/com.facebook.katana/app_light_prefs/com.facebook.katana/";
            var auth =
                await _ld.ExecuteAdbAsync(_index, prefix + "authentication", _path, ct) ?? "";
            var ownerRaw =
                await _ld.ExecuteAdbAsync(_index, prefix + "underlying_account", _path, ct) ?? "";
            var owner = PFlowGraphIdentityReader.Field(
                PFlowGraphIdentityReader.Clean(ownerRaw),
                "underlying_account_uid"
            );
            if (string.IsNullOrEmpty(owner))
                owner = PFlowGraphIdentityReader.Field(
                    PFlowGraphIdentityReader.Clean(auth),
                    "underlying_account_uid"
                );
            if (string.IsNullOrEmpty(owner))
                owner = PFlowGraphIdentityReader.Field(
                    PFlowGraphIdentityReader.Clean(auth),
                    "user_id"
                );
            var token = PFlowGraphIdentityReader.Token(auth);
            if (token.Length == 0 || owner.Length == 0)
            {
                _proofs.Clear();
                return null;
            }
            if (_process != pid || _owner != owner)
                _proofs.Clear();
            _process = pid;
            _owner = owner;
            var fingerprint = PFlowGraphIdentityReader.Fingerprint(token, owner);
            var actor = PFlowGraphIdentityReader.Field(
                PFlowGraphIdentityReader.Clean(auth),
                "user_id"
            );
            var key = fingerprint + ":" + actor;
            async Task<bool> ActorUnchanged()
            {
                var freshAuth =
                    await _ld.ExecuteAdbAsync(_index, prefix + "authentication", _path, ct) ?? "";
                return PFlowGraphIdentityReader.Field(
                        PFlowGraphIdentityReader.Clean(freshAuth),
                        "user_id"
                    ) == actor;
            }
            var binding = new PFlowAccountBinding(
                "fetch-session",
                _index,
                "LD-" + _index,
                "com.facebook.katana",
                owner
            );
            if (_proofs.TryGetValue(key, out var proof))
            {
                var reused = await proof.TryReuseAsync(binding, pid, _ld, _path, ct);
                if (reused != null && await ActorUnchanged())
                {
                    await PFlowTrace.SessionAsync(
                        _index,
                        "fetch.graph.reused",
                        "nativeSessionVerified=true; Graph calls=0",
                        ct
                    );
                    return reused;
                }
                _proofs.Clear();
            }
            var graph = await fresh();
            var afterPid = (
                await _ld.ExecuteAdbAsync(_index, "shell pidof com.facebook.katana", _path, ct)
                ?? ""
            ).Trim();
            if (
                graph == null
                || afterPid != pid
                || graph.UnderlyingUid != owner
                || graph.AuthenticationFingerprint != fingerprint
                || !await ActorUnchanged()
            )
            {
                _proofs.Clear();
                return null;
            }
            var next = PFlowGraphActivationProof.Create(binding, pid, graph);
            if (next == null || await next.TryReuseAsync(binding, pid, _ld, _path, ct) == null)
            {
                _proofs.Clear();
                return null;
            }
            _proofs[key] = next;
            await PFlowTrace.SessionAsync(
                _index,
                "fetch.graph.fresh",
                "freshActorVerified=true; duplicateStageReadsShared=true",
                ct
            );
            return graph;
        }
        catch
        {
            _proofs.Clear();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _proofs.Clear();
        if (ReferenceEquals(Slot.Value, this))
            Slot.Value = _previous;
    }
}
