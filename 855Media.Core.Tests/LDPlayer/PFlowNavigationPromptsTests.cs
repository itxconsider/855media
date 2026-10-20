using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowNavigationPromptsTests
{
    // Reconstructed from the supplied screenshot; not a captured device hierarchy.
    private static string Streak()
    {
        XElement Node(string label, bool clickable, string bounds) =>
            new(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", label),
                new XAttribute("clickable", clickable ? "true" : "false"),
                new XAttribute("enabled", "true"),
                new XAttribute("bounds", bounds)
            );
        var root = Node("", false, "[0,0][280,500]");
        var card = Node("", false, "[0,220][280,490]");
        card.Add(
            Node("You've started a weekly reels streak", false, "[20,320][260,350]"),
            Node("View my reels achievements", true, "[14,410][268,440]"),
            Node("Don't show me these updates again", true, "[14,450][268,480]")
        );
        root.Add(card);
        return new XElement("hierarchy", root).ToString();
    }

    [Fact]
    public void ExactStreakCardOnlyChoosesStopUpdates()
    {
        Assert.Equal((141, 465), PFlowNavigationPrompts.StreakDismissControl(Streak()));
        Assert.Equal(
            (141, 465),
            PFlowNavigationPrompts.StreakDismissControl(
                Streak().Replace("You've", "You’ve").Replace("Don't", "Don’t")
            )
        );
    }

    [Theory]
    [InlineData("title")]
    [InlineData("achievement")]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("duplicate")]
    public void IncompleteOrUnusablePopupIsNotDismissed(string change)
    {
        var doc = XDocument.Parse(Streak());
        var dismiss = doc.Descendants("node")
            .Single(n => n.Attribute("text")?.Value == "Don't show me these updates again");
        switch (change)
        {
            case "title":
                doc.Descendants("node")
                    .Single(n =>
                        n.Attribute("text")?.Value == "You've started a weekly reels streak"
                    )
                    .SetAttributeValue("text", "Different notice");
                break;
            case "achievement":
                doc.Descendants("node")
                    .Single(n => n.Attribute("text")?.Value == "View my reels achievements")
                    .Remove();
                break;
            case "foreign":
                foreach (var n in doc.Descendants("node"))
                    n.SetAttributeValue("package", "another.app");
                break;
            case "input":
                doc.Root!.Add(
                    new XElement("node", new XAttribute("class", "android.widget.EditText"))
                );
                break;
            case "hidden":
                dismiss.Parent!.SetAttributeValue("visible-to-user", "false");
                break;
            case "disabled":
                dismiss.SetAttributeValue("enabled", "false");
                break;
            case "duplicate":
                var other = new XElement(dismiss);
                other.SetAttributeValue("bounds", "[14,380][268,400]");
                dismiss.Parent!.Add(other);
                break;
        }
        Assert.Null(PFlowNavigationPrompts.StreakDismissControl(doc.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DismissesOnceAndStopsIfPopupStays(bool stuck)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PopupDevice>();
        var device = (PopupDevice)(object)ld;
        device.Fresh = stuck
            ? Streak()
            : "<hierarchy><node package='com.facebook.katana'/></hierarchy>";
        if (stuck)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PFlowNavigationPrompts.DismissAsync(
                    ld,
                    23,
                    Streak(),
                    null,
                    default,
                    (_, _) => Task.CompletedTask
                )
            );
        else
            Assert.Equal(
                device.Fresh,
                await PFlowNavigationPrompts.DismissAsync(
                    ld,
                    23,
                    Streak(),
                    null,
                    default,
                    (_, _) => Task.CompletedTask
                )
            );
        Assert.Equal(1, device.Taps);
    }

    public class PopupDevice : DispatchProxy
    {
        public string Fresh = "";
        public int Taps;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TapAsync")
            {
                Assert.Equal(141, args![1]);
                Assert.Equal(465, args[2]);
                Taps++;
                return Task.FromResult(true);
            }
            if (method.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(Fresh);
            throw new NotSupportedException(method.Name);
        }
    }
}
