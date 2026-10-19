using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public partial class LDPlayerService
{
    public async Task<PFlowHelperHealth?> Ensure885HelperAsync(
        int index,
        string? path,
        CancellationToken ct
    )
    {
        // Use the shell UID, never the ATX process identity, for the protected provider.
        const string query =
            "shell content query --uri content://com.media885.helper.status/status";
        var health = PFlowHelperHealth.Parse(
            await ExecuteDirectDeviceAdbAsync(index, query, path, ct)
        );
        if (health == null)
        {
            var package = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "tools", "885helper", "885Helper.apk"),
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "tools",
                    "885helper",
                    "885Helper.apk"
                ),
            }.FirstOrDefault(File.Exists);
            if (package == null)
            {
                await PFlowTrace.SessionAsync(
                    index,
                    "helper885.unavailable",
                    "Packaged 885Helper.apk was not found; retaining Android/ADB checks.",
                    ct
                );
                return null;
            }
            var result = await ExecuteDirectDeviceAdbAsync(
                index,
                $"install -r \"{package}\"",
                path,
                ct
            );
            if (
                !result
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Any(line => line.Trim() == "Success")
            )
            {
                await PFlowTrace.SessionAsync(
                    index,
                    "helper885.install-failed",
                    "885Helper installation was not acknowledged; retaining Android/ADB checks.",
                    ct
                );
                return null;
            }
            health = PFlowHelperHealth.Parse(
                await ExecuteDirectDeviceAdbAsync(index, query, path, ct)
            );
        }
        if (health != null)
        {
            await ExecuteDirectDeviceAdbAsync(
                index,
                $"shell content update --uri content://com.media885.helper.status/status --bind deviceLabel:s:LD-{index}",
                path,
                ct
            );
            await PFlowTrace.SessionAsync(
                index,
                "helper885.ready",
                $"protocol={health.ProtocolVersion}; heartbeat=true; internetValidated={health.InternetValidated}; activeNetworkVpn={health.ActiveNetworkVpn}; keyboardInstalled={health.AdbKeyboardInstalled}; keyboardActive={health.AdbKeyboardActive}; statusError={health.StatusError ?? "none"}",
                ct
            );
        }
        else
            await PFlowTrace.SessionAsync(
                index,
                "helper885.unresponsive",
                "No valid helper status response; retaining Android/ADB checks.",
                ct
            );
        return health;
    }
}
