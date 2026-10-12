using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Complete only the user's requested, observed location step after login. Other
// permissions/checkpoints cannot be accepted through a generic Continue/Allow tap.
public sealed class PFlowNativeLoginOnboarding(
    ILDPlayerService ld,
    string path,
    Func<TimeSpan, CancellationToken, Task>? delay = null
)
{
    private readonly PFlowSessionPreparation _preparation = new(ld, path);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public static (int X, int Y)? LocationPermissionControl(string? xml)
    {
        try
        {
            var nodes = XDocument
                .Parse(xml ?? "")
                .Descendants("node")
                .Where(n =>
                    (string?)n.Attribute("package")
                        is "com.android.packageinstaller"
                            or "com.google.android.packageinstaller"
                )
                .ToArray();
            if (
                !nodes.Any(n =>
                    (string?)n.Attribute("text")
                        is "Allow Facebook to access this device's location?"
                            or "Allow Facebook to access your location?"
                )
            )
                return null;
            var controls = nodes
                .Where(n =>
                    (string?)n.Attribute("resource-id")
                        == (string?)n.Attribute("package") + ":id/permission_allow_button"
                    && (string?)n.Attribute("enabled") == "true"
                    && (string?)n.Attribute("clickable") == "true"
                    && string.Equals(
                        (string?)n.Attribute("text"),
                        "Allow",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Select(ScreenTimeHierarchy.Center)
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            return controls.Length == 1 ? controls[0] : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    public async Task<string> CompleteLocationAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        var continued = false;
        var granted = false;
        var declinedSave = false;
        for (var i = 0; i < 12; i++)
        {
            await _preparation.RequireWorkerAsync(binding, ct);
            var activity =
                await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
            if (
                continued
                && (
                    activity.StartsWith("com.android.packageinstaller/", StringComparison.Ordinal)
                    || activity.StartsWith(
                        "com.google.android.packageinstaller/",
                        StringComparison.Ordinal
                    )
                )
                && activity.EndsWith(".GrantPermissionsActivity", StringComparison.Ordinal)
            )
            {
                var permission = await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct);
                var allow = LocationPermissionControl(permission);
                if (granted || allow == null)
                    throw new InvalidOperationException(
                        "Location permission screen changed; no other permission was granted."
                    );
                if (!await ld.TapAsync(binding.DeviceIndex, allow.Value.X, allow.Value.Y, path, ct))
                    throw new InvalidOperationException(
                        "Facebook location permission could not be confirmed."
                    );
                granted = true;
            }
            else
            {
                var xml = await _preparation.ReadFacebookScreenAsync(binding, ct);
                var kind = PFlowLoginScreen.Classify(xml);
                if (kind == PFlowLoginScreenKind.LocationServicesIntro)
                {
                    if (!continued)
                    {
                        var point = PFlowNativeLoginNavigator.Control(xml, "Continue");
                        if (
                            point == null
                            || !await ld.TapAsync(
                                binding.DeviceIndex,
                                point.Value.X,
                                point.Value.Y,
                                path,
                                ct
                            )
                        )
                            throw new InvalidOperationException(
                                "Facebook location setup could not be continued."
                            );
                        continued = true;
                    }
                }
                else if (continued && kind == PFlowLoginScreenKind.SaveLoginInfo)
                {
                    if (!declinedSave)
                    {
                        var point = PFlowNativeLoginNavigator.Control(xml, "Not now");
                        if (
                            point == null
                            || !await ld.TapAsync(
                                binding.DeviceIndex,
                                point.Value.X,
                                point.Value.Y,
                                path,
                                ct
                            )
                        )
                            throw new InvalidOperationException(
                                "Save-login prompt could not be dismissed."
                            );
                        declinedSave = true;
                    }
                }
                else
                    return xml;
            }
            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }
        throw new InvalidOperationException(
            "Facebook location setup did not finish; personal UID verification is still required."
        );
    }
}
