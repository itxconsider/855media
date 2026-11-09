using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed record PFlowAccountRemovalState(
    long Revision,
    IReadOnlyList<PFlowAccountBinding> Bindings
);

public sealed partial class PFlowAccountStore
{
    public async Task<PFlowAccountRemovalState> ReadRemovalStateAsync(
        CancellationToken ct = default
    )
    {
        using var gate = await LockAsync(ct);
        var document = await ReadAsync(ct);
        return new(document.RemovalRevision, document.RemovedBindings.ToArray());
    }

    // Only an explicit Fetch that has completed native owner discovery calls this.
    // Background reconciliation continues to respect all removal records.
    public async Task RestoreFetchedBindingAsync(
        PFlowAccountBinding binding,
        string name,
        long expectedRemovalRevision,
        CancellationToken ct = default
    )
    {
        binding.Validate();
        if (string.IsNullOrWhiteSpace(name) || binding.AppPackage != "com.facebook.katana")
            throw new InvalidOperationException(
                "A fetched personal name and supported app are required."
            );
        await UpdateDocumentAsync(
            document =>
            {
                if (document.RemovalRevision != expectedRemovalRevision)
                    throw new InvalidOperationException(
                        "Account removal changed during Fetch; run Fetch again to restore it explicitly."
                    );
                if (!document.RemovedBindings.Contains(binding))
                    throw new InvalidOperationException(
                        "The exact removed account binding changed during Fetch."
                    );
                if (
                    document.Accounts.Any(a =>
                        a.Binding == binding
                        || a.PreviousBindings.Contains(binding)
                        || a.TransferredFromBindings.Contains(binding)
                    )
                )
                    throw new InvalidOperationException(
                        "The account has an active or transferred mapping; Fetch cannot undo that assignment."
                    );
                document.RemovedBindings.Remove(binding);
                // A re-added account has no restored credentials, selections or queued work.
                document.Accounts.Add(new() { Binding = binding, Name = name });
            },
            ct
        );
    }

    public async Task<IReadOnlyList<PFlowAccountBinding>> RemovedBindingsAsync(
        CancellationToken ct = default
    )
    {
        using var gate = await LockAsync(ct);
        return (await ReadAsync(ct)).RemovedBindings.ToArray();
    }

    public async Task<string> RemoveAsync(
        PFlowAccountBinding expected,
        CancellationToken ct = default
    )
    {
        var credential = "";
        await UpdateDocumentAsync(
            document =>
            {
                var account =
                    document.Accounts.SingleOrDefault(a => a.Binding == expected)
                    ?? throw new InvalidOperationException(
                        "Saved account changed; refresh before removing it."
                    );
                credential = account.CredentialReference;
                foreach (
                    var binding in account
                        .PreviousBindings.Concat(account.TransferredFromBindings)
                        .Append(account.Binding)
                )
                    if (!document.RemovedBindings.Contains(binding))
                        document.RemovedBindings.Add(binding);
                document.Accounts.Remove(account);
                document.RemovalRevision = checked(document.RemovalRevision + 1);
            },
            ct
        );
        return credential;
    }
}
