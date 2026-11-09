using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowMapPromptTests
{
    [Theory]
    [InlineData("valid", 1)]
    [InlineData("wrong-title", 0)]
    [InlineData("missing-legal", 0)]
    [InlineData("foreign", 0)]
    [InlineData("disabled", 0)]
    [InlineData("duplicate", 0)]
    [InlineData("input", 0)]
    public async Task ExactCapturedMapDialogDismissesOnlyItsUniqueOk(string change, int taps)
    {
        var doc = XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowLayouts",
                "ld20-map-information-captured.xml"
            )
        );
        var ok = doc.Descendants("node")
            .Single(n =>
                (string?)n.Attribute("clickable") == "true"
                && (string?)n.Attribute("content-desc") == "OK"
            );
        if (change == "wrong-title")
            foreach (
                var a in doc.Descendants("node")
                    .Attributes()
                    .Where(a => a.Value == "Map information")
                    .ToArray()
            )
                a.Value = "Other information";
        if (change == "missing-legal")
            foreach (
                var a in doc.Descendants("node")
                    .Attributes()
                    .Where(a => a.Value == "Map data legal notices")
                    .ToArray()
            )
                a.Value = "Other notices";
        if (change == "foreign")
            foreach (var a in doc.Descendants("node").Attributes("package"))
                a.Value = "other.app";
        if (change == "disabled")
            ok.SetAttributeValue("enabled", "false");
        if (change == "duplicate")
        {
            doc.Root!.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.Button"),
                    new XAttribute("text", "OK"),
                    new XAttribute("clickable", "true"),
                    new XAttribute("enabled", "true"),
                    new XAttribute("bounds", "[188,620][531,690]")
                )
            );
        }
        if (change == "input")
            ok.Parent!.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.EditText")
                )
            );
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        await PFlowNavigationPrompts.DismissAsync(
            ld,
            900758,
            doc.ToString(),
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal(taps, fake.Taps);
        if (taps > 0)
            Assert.Equal((359, 754), fake.Point);
    }

    public class Device : DispatchProxy
    {
        public int Taps;
        public (int, int) Point;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TapAsync")
            {
                Taps++;
                Point = ((int)args![1]!, (int)args[2]!);
                return Task.FromResult(true);
            }
            if (method.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(
                    "<hierarchy><node package='com.facebook.katana' text='Home' bounds='[0,0][720,1280]'/></hierarchy>"
                );
            throw new NotSupportedException(method.Name);
        }
    }
}
