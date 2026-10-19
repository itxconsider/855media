using System;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Avalonia.Threading;

namespace _855Media.ViewModels.Components;

public partial class AutoPostViewModel
{
    private async Task ResolveLdInstallationAsync()
    {
        var changed = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var actual = PFlowDeviceRouting.ResolveInstallationDirectory(
                _ldPlayerService,
                LdFolder
            );
            if (string.Equals(actual, LdFolder, StringComparison.Ordinal))
                return false;
            LdFolder = actual;
            return true;
        });
        if (!changed)
            return;
        await PFlowFetchTrace.WriteAsync(
            -1,
            "installation.path.resolved",
            "Account mapping and LD commands now use " + LdFolder
        );
        try
        {
            var config = await Dispatcher.UIThread.InvokeAsync(ExportConfig);
            await AutoPostConfigStorage.SaveConfigAsync(config);
        }
        catch (Exception ex)
        {
            await PFlowFetchTrace.WriteAsync(-1, "installation.path.save-warning", ex.Message);
        }
    }
}
