using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Only dismiss the observed Android credential chooser. Never select a saved account/password.
public static class PFlowAndroidCredentialPrompt
{
    public const string Activity =
        "com.google.android.gms/com.google.android.gms.identitycredentials.ui.SignInCredentialChooserActivity";

    public static (int X, int Y)? CloseControl(string? xml)
    {
        try
        {
            var nodes = XDocument
                .Parse(xml ?? "")
                .Descendants("node")
                .Where(n => (string?)n.Attribute("package") == "com.google.android.gms")
                .ToArray();
            if (
                !nodes.Any(n =>
                    (string?)n.Attribute("text")
                        is "Use your saved password for Facebook"
                            or "Save password to sign in to Facebook?"
                ) || !nodes.Any(n => (string?)n.Attribute("text") == "Facebook")
            )
                return null;
            var controls = nodes
                .Where(n =>
                    (string?)n.Attribute("content-desc") == "Close"
                    && (string?)n.Attribute("enabled") == "true"
                )
                .Select(n =>
                    n.AncestorsAndSelf("node")
                        .FirstOrDefault(a =>
                            (string?)a.Attribute("package") == "com.google.android.gms"
                            && (string?)a.Attribute("clickable") == "true"
                            && (string?)a.Attribute("enabled") == "true"
                        )
                )
                .Where(n => n != null)
                .Select(n => ScreenTimeHierarchy.Center(n!))
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
}
