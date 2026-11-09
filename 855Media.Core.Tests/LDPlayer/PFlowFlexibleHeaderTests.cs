using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFlexibleHeaderTests
{
    [Fact]
    public async Task CapturedLd56BackgroundMenuConfirmsOwnerAndActivePage()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld56-menu-overlay-missing");
        Assert.Equal("Bros Cooking", PFlowPostingNameVerification.ActiveHeaderName(xml));
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNameResponseRecoveryTests.Device>();
        var fake = (PFlowNameResponseRecoveryTests.Device)(object)ld;
        fake.Loading = xml;
        var actor = await PFlowPostingNameVerification.ObserveAccountAsync(
            ld,
            Binding,
            "Marc Aurelius",
            null,
            default
        );
        Assert.Equal(new PFlowPostingNameVerification.Actor("Bros Cooking", "Page"), actor);
        Assert.Null(PFlowPostingNameVerification.PersonalShortcut(xml, "Marc Aurelius"));
        Assert.NotNull(
            PFlowPostingNameVerification.PersonalShortcut(
                xml,
                "Marc Aurelius",
                requireUsable: false
            )
        );
        Assert.Null(PFlowPostingNameVerification.PersonalShortcut(xml, "Other Account"));
        Assert.Null(
            PFlowPostingNameVerification.PersonalShortcut(
                xml.Replace("com.facebook.katana", "com.other.app"),
                "Marc Aurelius"
            )
        );
        var doc = XDocument.Parse(xml);
        var shortcut = doc.Descendants("node")
            .Single(n =>
                n.Attribute("content-desc")?.Value == "Marc Aurelius, switch into your profile"
            );
        shortcut.SetAttributeValue("enabled", "false");
        Assert.Null(PFlowPostingNameVerification.PersonalShortcut(doc.ToString(), "Marc Aurelius"));
        shortcut.SetAttributeValue("enabled", "true");
        shortcut.AddAfterSelf(new XElement(shortcut));
        Assert.Null(PFlowPostingNameVerification.PersonalShortcut(doc.ToString(), "Marc Aurelius"));
    }

    private static string MenuCapture() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowLayouts/ld64-lowered-menu-header.xml"
            )
        );

    private static XDocument ProfileCapture() =>
        XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld61-tail-talks-profile-title.xml"
            )
        );

    private static PFlowAccountBinding Binding =>
        new("fixture", 900764, "fixture-LD", "com.facebook.katana", "100000000000001");

    private static FacebookPageTarget Target(string name) =>
        new()
        {
            AccountBinding = Binding,
            PageId = "200000000000001",
            UserUid = Binding.OwnerUid,
            PageName = name,
            Type = "Page",
        };

    [Fact]
    public async Task CapturedLowerMenuCardConfirmsPageAndOwnerWithoutSwitchingOrWaiting()
    {
        var xml = MenuCapture();
        Assert.Equal("Mattea Page+", PFlowPostingNameVerification.ActiveHeaderName(xml));
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNameResponseRecoveryTests.Device>();
        var fake = (PFlowNameResponseRecoveryTests.Device)(object)ld;
        fake.Loading = xml;
        var actor = await PFlowPostingNameVerification.ObserveAccountAsync(
            ld,
            Binding,
            "Альона Лисенко",
            null,
            default
        );
        Assert.Equal(new PFlowPostingNameVerification.Actor("Mattea Page+", "Page"), actor);
        var target = Target("Mattea Page+");
        var proof = await PFlowPostingNameVerification.ActivateWithResponseWaitAsync(
            ld,
            Binding,
            target,
            [target],
            null,
            default,
            null,
            (_, _) => throw new Exception("Readable active card must not wait"),
            () => DateTimeOffset.UtcNow
        );
        Assert.True(proof.UsesDatabaseName);
        Assert.Equal(0, fake.Stops + fake.Launches + fake.ForbiddenReads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0.5)]
    [InlineData(1.5)]
    public void MenuCardScalesAndIgnoresOtherShortcutNames(double scale)
    {
        var doc = XDocument.Parse(MenuCapture());
        Scale(doc, scale);
        Assert.Equal("Mattea Page+", PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData("Metsätalous", 242, 430, 632, 420, true)]
    [InlineData("Michael Flynn", 24, 360, 380, 570, true)]
    [InlineData("Taoxi Brothers", 242, 510, 632, 612, false)]
    [InlineData("Mattea Page+", 24, 320, 340, 670, true)]
    [InlineData("Bushcraft\nimprovisation", 120, 510, 632, 612, false)]
    public void OwnProfileNamesCanBeBesideAvatarCentredOrLeftAligned(
        string name,
        int left,
        int right,
        int switchLeft,
        int top,
        bool notificationSuffix
    )
    {
        var doc = ProfileCapture();
        foreach (var n in doc.Descendants("node"))
        {
            if (
                n.Attribute("text")?.Value.Trim() == "Tail Talks+"
                && n.Attribute("bounds")?.Value.StartsWith("[242,612]") == true
            )
            {
                n.SetAttributeValue("text", name);
                n.SetAttributeValue("content-desc", name);
                n.SetAttributeValue(
                    "bounds",
                    $"[{left},{top}][{right},{top + (name.Contains('\n') ? 100 : 55)}]"
                );
            }
            if (n.Attribute("content-desc")?.Value == "Open profile switcher")
            {
                if (notificationSuffix)
                    n.SetAttributeValue(
                        "content-desc",
                        "Open profile switcher, you have notifications"
                    );
                n.SetAttributeValue(
                    "bounds",
                    $"[{switchLeft},{top}][{switchLeft + 64},{top + 64}]"
                );
            }
            if (n.Attribute("text")?.Value == "Digital creator")
                n.SetAttributeValue("bounds", $"[240,{top + 110}][480,{top + 145}]");
        }
        Assert.Equal(name, PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData("Metsätalous")]
    [InlineData("Mattea Page+")]
    [InlineData("Tail Talks+")]
    public void VisitorProfileWithFollowMessageAndNoSwitcherNeverConfirmsActor(string name)
    {
        var doc = ProfileCapture();
        doc.Descendants("node")
            .Where(n => n.Attribute("content-desc")?.Value == "Open profile switcher")
            .Remove();
        foreach (
            var n in doc.Descendants("node")
                .Where(n => n.Attribute("text")?.Value.Trim() == "Tail Talks+")
        )
            n.SetAttributeValue("text", name);
        doc.Root!.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "Follow")
            ),
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "Message")
            )
        );
        Assert.Null(PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
        doc.Root.Add(
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("text", "Admin quick access")
            )
        );
        Assert.Null(PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData("no-switcher")]
    [InlineData("hidden-switcher")]
    [InlineData("disabled-switcher")]
    [InlineData("wrong-row")]
    [InlineData("inside-shortcuts")]
    [InlineData("ambiguous")]
    [InlineData("input")]
    [InlineData("foreign")]
    public void LowerMenuCardStillNeedsAnUnambiguousUsableHeaderRegion(string mutation)
    {
        var doc = XDocument.Parse(MenuCapture());
        var sw = doc.Descendants("node")
            .Single(n =>
                (n.Attribute("content-desc")?.Value ?? "").StartsWith("Open profile switcher,")
            );
        var card = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Mattea Page+, see your profile");
        if (mutation == "no-switcher")
            sw.Remove();
        if (mutation == "hidden-switcher")
            sw.SetAttributeValue("visible-to-user", "false");
        if (mutation == "disabled-switcher")
            sw.SetAttributeValue("enabled", "false");
        if (mutation == "wrong-row")
            sw.SetAttributeValue("bounds", "[584,800][672,888]");
        if (mutation == "inside-shortcuts")
            card.SetAttributeValue("bounds", "[48,600][386,680]");
        if (mutation == "ambiguous")
        {
            var other = new XElement(card);
            other.SetAttributeValue("content-desc", "Other Page, see your profile");
            card.Parent!.Add(other);
        }
        if (mutation == "input")
            doc.Root!.Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.EditText")
                )
            );
        if (mutation == "foreign")
            doc.Root!.Add(new XElement("node", new XAttribute("package", "other.app")));
        Assert.Null(PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    private static void Scale(XDocument doc, double scale)
    {
        foreach (var b in doc.Descendants("node").Attributes("bounds"))
            b.Value = System.Text.RegularExpressions.Regex.Replace(
                b.Value,
                @"\d+",
                m => ((int)(int.Parse(m.Value) * scale)).ToString()
            );
    }
}
