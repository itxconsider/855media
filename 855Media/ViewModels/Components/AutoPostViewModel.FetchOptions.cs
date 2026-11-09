using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    [ObservableProperty]
    private bool _skipVerifiedFetchAccounts;

    [ObservableProperty]
    private bool _skipVerifiedFetchPages;

    private async Task<LDInstanceInfo[]> ReadAssignedFetchInstanceAsync(
        int index,
        CancellationToken ct
    )
    {
        var rows = _ldPlayerService is LDPlayerService native
            ? await native.GetAssignedInstancesAsync(index, LdFolder, ct)
            : await _ldPlayerService.GetInstancesAsync(LdFolder, ct);
        return rows.Where(i => i.Index == index).ToArray();
    }
}
