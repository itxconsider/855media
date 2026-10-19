using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowAccountStore
{
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
            },
            ct
        );
        return credential;
    }
}
