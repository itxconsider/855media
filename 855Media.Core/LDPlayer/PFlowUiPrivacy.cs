using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowUiPrivacy
{
    // Unknown hierarchy cannot rule out a password/code screen.
    public static bool ContainsInput(string? xml)
    {
        try
        {
            var nodes = XDocument.Parse(xml ?? "").Descendants("node").ToArray();
            return nodes.Length == 0
                || nodes.Any(n =>
                    (string?)n.Attribute("class") == "android.widget.EditText"
                    || (string?)n.Attribute("password") == "true"
                );
        }
        catch (System.Xml.XmlException)
        {
            return true;
        }
    }

    public static string RedactForDiagnostics(string? xml)
    {
        try
        {
            var doc = XDocument.Parse(xml ?? "");
            var nodes = doc.Descendants("node").ToArray();
            var authentication = nodes.Any(n =>
                (string?)n.Attribute("password") == "true"
                || (
                    (string?)n.Attribute("class") == "android.widget.EditText"
                    && ((string?)n.Attribute("content-desc") ?? "").Split(',')[0]
                        is "Code"
                            or "Password"
                            or "Mobile number or email"
                )
            );
            foreach (
                var node in nodes.Where(n =>
                    authentication
                    || (string?)n.Attribute("class") == "android.widget.EditText"
                    || (string?)n.Attribute("package") == "com.google.android.gms"
                )
            )
            {
                node.SetAttributeValue("text", "");
                node.SetAttributeValue("content-desc", "[input redacted]");
            }
            return doc.ToString(SaveOptions.DisableFormatting);
        }
        catch (System.Xml.XmlException)
        {
            return "<hierarchy />";
        }
    }
}
