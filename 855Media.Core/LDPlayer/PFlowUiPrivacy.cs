using System;
using System.Linq;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public static class PFlowUiPrivacy
{
    internal static bool IsEditableClass(string? name) =>
        name
            is "android.widget.EditText"
                or "android.widget.AutoCompleteTextView"
                or "android.widget.MultiAutoCompleteTextView";

    // Unknown hierarchy cannot rule out a password/code screen.
    public static bool ContainsInput(string? xml)
    {
        try
        {
            var nodes = FacebookHierarchyCache.ParseStrict(xml ?? "").Descendants("node").ToArray();
            return nodes.Length == 0
                || nodes.Any(n =>
                    IsEditableClass((string?)n.Attribute("class"))
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
                    IsEditableClass((string?)n.Attribute("class"))
                    && ((string?)n.Attribute("content-desc") ?? "").Split(',')[0]
                        is "Code"
                            or "Password"
                            or "Mobile number or email"
                )
            );
            foreach (
                var node in nodes.Where(n =>
                    authentication
                    || IsEditableClass((string?)n.Attribute("class"))
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
