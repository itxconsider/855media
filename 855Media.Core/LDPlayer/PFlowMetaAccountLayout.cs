using System;
using System.Linq;

namespace _855Media.Core.LDPlayer;

internal static class PFlowMetaAccountLayout
{
    private static bool Has(string? xml, string label) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                !n.AncestorsAndSelf("node")
                    .Any(a => a.Attribute("visible-to-user")?.Value == "false")
                && new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(v =>
                    string.Equals(
                        System.Text.RegularExpressions.Regex.Replace(v ?? "", @"\s+", " ").Trim(),
                        label,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );

    private static bool ReadOnly(string? xml, bool search = false)
    {
        var nodes = ScreenTimeHierarchy.Nodes(xml);
        return nodes.Length > 0
            && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            && !nodes.Any(n =>
                n.Attribute("password")?.Value == "true"
                || (
                    n.Attribute("package")?.Value is { Length: > 0 } p
                    && p is not ("com.facebook.katana" or "com.android.systemui")
                )
                || (
                    n.Attribute("class")?.Value == "android.widget.EditText"
                    && !(
                        search
                        && new[]
                        {
                            n.Attribute("text")?.Value,
                            n.Attribute("content-desc")?.Value,
                            n.Attribute("hint")?.Value,
                        }.Any(v =>
                            string.Equals(
                                v?.Trim(),
                                "Search settings",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                )
            );
    }

    internal static bool IsSettings(string? xml) =>
        ReadOnly(xml, search: true)
        && Has(xml, "Settings & privacy")
        && Has(xml, "Your account")
        && Has(xml, "Meta Account")
        && Has(xml, "Subscriptions")
        && Has(xml, "Tools and resources");

    internal static bool IsHome(string? xml) =>
        ReadOnly(xml)
        && Has(xml, "Meta Account")
        && Has(xml, "Login and security")
        && Has(xml, "Your information and permissions")
        && !Has(xml, "Settings & privacy");

    internal static bool IsInformation(string? xml) =>
        ReadOnly(xml)
        && Has(xml, "Your information and permissions")
        && Has(xml, "Birthday")
        && Has(xml, "Export your information")
        && Has(xml, "Access your information")
        && Has(xml, "Search history");

    internal static bool IsBirthday(string? xml) =>
        ReadOnly(xml)
        && Has(xml, "Birthday")
        && Has(xml, "Edit")
        && Has(xml, "Who can see your birthday")
        && !Has(xml, "Your information and permissions");

    internal static string ReadBirthday(string xml)
    {
        if (!IsBirthday(xml))
            return "";
        var (_, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        var dates = new System.Collections.Generic.HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (
            var edit in ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    new[] { n.Attribute("text")?.Value, n.Attribute("content-desc")?.Value }.Any(
                        v => v?.Trim() == "Edit"
                    )
                )
        )
        foreach (var row in edit.Ancestors("node").Take(3))
        {
            var bounds = System.Text.RegularExpressions.Regex.Match(
                row.Attribute("bounds")?.Value ?? "",
                @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]"
            );
            if (
                !bounds.Success
                || height <= 0
                || int.Parse(bounds.Groups[4].Value) - int.Parse(bounds.Groups[2].Value)
                    > height / 4
            )
                continue;
            foreach (
                var node in row.DescendantsAndSelf("node")
                    .Where(n =>
                        n.Attribute("package")?.Value == "com.facebook.katana"
                        && !n.AncestorsAndSelf("node")
                            .Any(a => a.Attribute("visible-to-user")?.Value == "false")
                    )
            )
            foreach (
                var value in new[]
                {
                    node.Attribute("text")?.Value,
                    node.Attribute("content-desc")?.Value,
                }
            )
            {
                var date = PFlowProfileDates.ValidDate(value ?? "");
                if (date.Length > 0 && date == value?.Trim())
                    dates.Add(date);
            }
            break;
        }
        return dates.Count == 1 ? dates.Single() : "";
    }
}
