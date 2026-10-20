using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Used only while opening the Reel picker, before selecting any media.
internal static class PFlowMediaAccess
{
    internal sealed record Control(string Kind, int X, int Y);

    internal static Control? FindControl(string xml, string appPackage)
    {
        var nodes = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                !n.AncestorsAndSelf("node")
                    .Any(a => (string?)a.Attribute("visible-to-user") == "false")
            )
            .ToArray();
        bool Label(XElement n, string value) =>
            new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }.Any(s =>
                string.Equals(
                    System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim(),
                    value,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        Control? Unique(string kind, XElement[] controls)
        {
            var points = controls
                .Where(n =>
                    (string?)n.Attribute("enabled") == "true"
                    && (string?)n.Attribute("clickable") == "true"
                )
                .Select(PFlowStoryLayout.ControlCenter)
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            return points.Length == 1 ? new(kind, points[0]!.Value.X, points[0]!.Value.Y) : null;
        }
        var facebook = nodes.Where(n => (string?)n.Attribute("package") == appPackage).ToArray();
        var facebookForeground = nodes.All(n =>
            (string?)n.Attribute("package") is null or "" or "com.android.systemui"
            || (string?)n.Attribute("package") == appPackage
        );
        if (
            facebookForeground
            && facebook.Any(n =>
                Label(n, "Allow Facebook to use your phone’s storage?")
                || Label(n, "Allow Facebook to use your phone's storage?")
            )
            && facebook.Any(n => Label(n, "Deny"))
        )
            return Unique("facebook-storage", facebook.Where(n => Label(n, "Allow")).ToArray());
        if (
            facebook.Any(n => Label(n, "Create reel"))
            && facebook.Any(n => Label(n, "Allow access to your camera roll"))
            && !nodes.Any(n => Label(n, "Deny"))
            && facebookForeground
        )
            return Unique(
                "camera-roll",
                facebook
                    .Where(n => Label(n, "Allow access"))
                    .Select(n =>
                        n.AncestorsAndSelf("node")
                            .FirstOrDefault(a =>
                                (string?)a.Attribute("package") == appPackage
                                && (string?)a.Attribute("clickable") == "true"
                                && (string?)a.Attribute("enabled") == "true"
                            )
                    )
                    .OfType<XElement>()
                    .Distinct()
                    .ToArray()
            );

        foreach (
            var systemPackage in new[]
            {
                "com.android.packageinstaller",
                "com.google.android.packageinstaller",
                "com.android.permissioncontroller",
                "com.google.android.permissioncontroller",
            }
        )
        {
            var system = nodes
                .Where(n => (string?)n.Attribute("package") == systemPackage)
                .ToArray();
            if (
                !system.Any(n =>
                    new[]
                    {
                        "Allow Facebook to access photos, media, and files on your device?",
                        "Allow Facebook to access photos and media on your device?",
                        "Allow Facebook to access photos and videos on this device?",
                    }.Any(message => Label(n, message))
                )
            )
                continue;
            return Unique(
                "android-media",
                system
                    .Where(n =>
                        (
                            (string?)n.Attribute("resource-id")
                                == systemPackage + ":id/permission_allow_button"
                            && Label(n, "Allow")
                        )
                        || (
                            (string?)n.Attribute("resource-id")
                                == systemPackage + ":id/permission_allow_all_button"
                            && Label(n, "Allow all")
                        )
                    )
                    .ToArray()
            );
        }
        return null;
    }

    internal static async Task<string> CompleteAsync(
        ILDPlayerService ld,
        int index,
        string appPackage,
        string? path,
        string xml,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        delay ??= Task.Delay;
        string? lastTapped = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var control = FindControl(xml, appPackage);
            if (control == null)
                return xml;
            if (control.Kind != lastTapped)
            {
                if (!await ld.TapAsync(index, control.X, control.Y, path, ct))
                    throw new InvalidOperationException(
                        "Facebook media access could not be confirmed; no media selected."
                    );
                lastTapped = control.Kind;
                await PFlowTrace.SessionAsync(
                    index,
                    "reel.media-access",
                    "prompt=" + control.Kind,
                    ct
                );
            }
            await delay(TimeSpan.FromMilliseconds(500), ct);
            xml = await ld.DumpUiHierarchyAsync(index, path, ct) ?? "";
        }
        throw new InvalidOperationException(
            "Facebook media access prompt did not finish; no media selected."
        );
    }
}
