using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public sealed partial class PFlowAccountStore
{
    // Called only after successful complete account Fetch and Page persistence.
    // Existing account options, including restored-account defaults, are retained.
    public Task RegisterFetchedAccountAsync(
        PFlowAccountBinding binding,
        string name,
        long removalRevision,
        CancellationToken ct = default
    )
    {
        binding.Validate();
        return UpdateDocumentAsync(
            document =>
            {
                if (document.RemovedBindings.Contains(binding))
                    throw new InvalidOperationException(
                        "This account was removed during Fetch. Fetch again to restore it."
                    );
                if (document.Accounts.Any(a => a.Binding == binding))
                    return;
                if (document.RemovalRevision != removalRevision)
                    throw new InvalidOperationException(
                        "Account mappings changed during Fetch. Refresh and try again."
                    );
                document.Accounts.Add(
                    new()
                    {
                        Binding = binding,
                        Name = name,
                        SelectedForDiscovery = true,
                        SelectedForScreenTime = true,
                        SelectedForPosting = true,
                    }
                );
            },
            ct
        );
    }
}
