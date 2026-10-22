using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

// Reuses only the pre-switch identity read. The destination still requires fresh Graph.
public sealed class PFlowGraphActivationProof
{
    private readonly PFlowGraphIdentity _identity;
    private readonly PFlowNativeIdentityScope _session;
    private readonly FacebookPageTarget _actor;
    private bool _valid = true;

    private PFlowGraphActivationProof(
        PFlowGraphIdentity identity,
        PFlowNativeIdentityScope session,
        FacebookPageTarget actor
    ) => (_identity, _session, _actor) = (identity, session, actor);

    public static PFlowGraphActivationProof? Create(
        PFlowAccountBinding binding,
        string process,
        PFlowGraphIdentity graph
    )
    {
        var actor = new FacebookPageTarget
        {
            PageId = graph.Id,
            PersonaUid = graph.Id,
            UserUid = graph.UnderlyingUid,
            PageName = graph.Name,
            Type = graph.Id == binding.OwnerUid ? "Profile" : "Page",
        };
        var session = PFlowNativeIdentityScope.FromVerifiedGraph(binding, process, actor, graph);
        return session == null ? null : new(graph, session, actor);
    }

    public async Task<PFlowGraphIdentity?> TryReuseAsync(
        PFlowAccountBinding binding,
        string process,
        ILDPlayerService ld,
        string? path,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (
                _valid
                && _session.Matches(binding, process, _actor)
                && await _session.SessionUnchangedAsync(ld, path, ct)
            )
                return _identity;
            _valid = false;
            return null;
        }
        catch
        {
            _valid = false;
            throw;
        }
    }
}
