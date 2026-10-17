using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace _855Media.Core.LDPlayer;

// One-use handoff from account preparation to the immediately following operation.
// Never persisted; a changed process, binding or captured UI requires a fresh read.
public sealed class PFlowStartupIdentity
{
    private sealed record Proof(
        PFlowAccountBinding Binding,
        string Process,
        string XmlHash,
        PFlowGraphIdentity Identity,
        DateTime Utc
    );

    private Proof? _proof;

    public PFlowStartupIdentity(
        PFlowAccountBinding binding,
        string process,
        string xml,
        PFlowGraphIdentity identity,
        DateTime utc
    )
    {
        if (
            !string.IsNullOrWhiteSpace(process)
            && process
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(p => p.All(char.IsAsciiDigit))
            && !PFlowUiPrivacy.ContainsInput(xml)
        )
            _proof = new(binding, process, Hash(xml), identity, utc);
    }

    public PFlowGraphIdentity? Consume(
        PFlowAccountBinding binding,
        string process,
        string? xml,
        DateTime utc
    )
    {
        var proof = Interlocked.Exchange(ref _proof, null);
        return
            proof != null
            && xml != null
            && proof.Binding == binding
            && proof.Process == process
            && utc - proof.Utc is { TotalSeconds: >= 0 and <= 15 }
            && proof.XmlHash == Hash(xml)
            ? proof.Identity
            : null;
    }

    private static string Hash(string xml) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml)));
}
