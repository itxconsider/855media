using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowStructuralHeaderTests
{
    private static string Ld4Capture() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld4-profile-unseen-story-header.xml"
            )
        );

    [Fact]
    public void CapturedUnseenStoryAvatarStillReadsExactSelectedName() =>
        Assert.Equal("Fixture Page", PFlowPostingNameVerification.ActiveHeaderName(Ld4Capture()));

    [Theory]
    [InlineData("Profile picture")]
    [InlineData("Profile picture with any future story status")]
    [InlineData("Profile picture, new accessory")]
    public void AvatarStatusDoesNotDetermineActor(string description)
    {
        var xml = Ld4Capture().Replace("Profile picture with unseen stories", description);
        Assert.Equal("Fixture Page", PFlowPostingNameVerification.ActiveHeaderName(xml));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("unrelated-label")]
    [InlineData("duplicate-switcher")]
    [InlineData("conflict")]
    public void CapturedStoryAvatarRetainsAllNameAndControlGuards(string mode)
    {
        var doc = XDocument.Parse(Ld4Capture());
        var avatar = doc.Descendants("node")
            .Single(n =>
                (string?)n.Attribute("content-desc") == "Profile picture with unseen stories"
            );
        if (mode == "disabled")
            avatar.SetAttributeValue("enabled", "false");
        if (mode == "hidden")
            avatar.SetAttributeValue("visible-to-user", "false");
        if (mode == "unrelated-label")
            avatar.SetAttributeValue("content-desc", "Profile pictures gallery");
        if (mode == "duplicate-switcher")
        {
            var sw = doc.Descendants("node")
                .Single(n => (string?)n.Attribute("content-desc") == "Open profile switcher");
            sw.Parent!.Add(new XElement(sw));
        }
        if (mode == "conflict")
        {
            var title = doc.Descendants("node")
                .Single(n => (string?)n.Attribute("text") == "Fixture Page");
            var other = new XElement(title);
            other.SetAttributeValue("text", "Different actor");
            title.Parent!.Add(other);
        }
        Assert.Null(PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    private const string Name = "YeeYee's Life";

    private static XDocument Capture() =>
        XDocument.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld3-centered-profile-header.xml"
            )
        );

    private static XElement Title(XDocument doc) =>
        doc.Descendants("node").Single(n => n.Attribute("text")?.Value == Name);

    [Fact]
    public void CapturedCenteredProfileReadsExactName() =>
        Assert.Equal(Name, PFlowPostingNameVerification.ActiveHeaderName(Capture().ToString()));

    [Theory]
    [InlineData("Get verified", "Â ")]
    [InlineData("New unrelated action", "★")]
    [InlineData("任意の操作", "●")]
    [InlineData("Any future badge", "123")]
    [InlineData("", "")]
    public void AccessoryWordingAndAggregateWrapperDoNotDetermineActor(
        string action,
        string decoration
    )
    {
        var doc = Capture();
        var title = Title(doc);
        var wrapper = title.Parent!;
        wrapper.SetAttributeValue("text", Name + " " + decoration);
        wrapper.SetAttributeValue("content-desc", Name + " " + action);
        var badge = wrapper
            .Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Get verified");
        badge.SetAttributeValue("content-desc", action);
        badge.SetAttributeValue("text", decoration);
        if (action.Length == 0)
            badge.Remove();
        // Extra aggregation levels may change independently of the name control.
        var nested = new XElement("node", wrapper.Attributes());
        title.ReplaceWith(nested);
        nested.Add(title);
        Assert.Equal(Name, PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("truncated")]
    [InlineData("foreign")]
    [InlineData("duplicate-switcher")]
    [InlineData("different-name")]
    public void StructuralReadingRetainsIdentityGuards(string mutation)
    {
        var doc = Capture();
        var title = Title(doc);
        if (mutation == "disabled")
            title.Parent!.SetAttributeValue("enabled", "false");
        if (mutation == "hidden")
            title.Parent!.SetAttributeValue("visible-to-user", "false");
        if (mutation == "truncated")
            title.SetAttributeValue("text", "YeeYee...");
        if (mutation == "foreign")
            title.SetAttributeValue("package", "other.app");
        if (mutation == "duplicate-switcher")
        {
            var sw = doc.Descendants("node")
                .Single(n =>
                    n.Attribute("content-desc")?.Value == "Open profile switcher"
                    && n.Attribute("clickable")?.Value == "true"
                );
            sw.Parent!.Add(new XElement(sw));
        }
        if (mutation == "different-name")
        {
            var other = new XElement(title);
            other.SetAttributeValue("text", "Different actor");
            title.Parent!.Add(other);
        }
        Assert.Null(PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData("ld3-centered-profile-header", Name)]
    [InlineData("ld4-profile-unseen-story-header", "Fixture Page")]
    public async Task CapturedHeaderActivatesWithoutNavigationOrBadgeProof(
        string fixture,
        string name
    )
    {
        var binding = new PFlowAccountBinding(
            "fixture",
            900763,
            "fixture-LD",
            "com.facebook.katana",
            "owner"
        );
        var target = new FacebookPageTarget
        {
            PageId = "page",
            UserUid = "owner",
            PageName = name,
            Type = "Page",
            AccountBinding = binding,
        };
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNameResponseRecoveryTests.Device>();
        var fake = (PFlowNameResponseRecoveryTests.Device)(object)ld;
        fake.Loading = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures/PFlowScreenTime", fixture + ".xml")
        );
        var proof = await PFlowPostingNameVerification.ActivateWithResponseWaitAsync(
            ld,
            binding,
            target,
            [target],
            null,
            default,
            null,
            (_, _) => throw new InvalidOperationException("No wait required"),
            () => DateTimeOffset.UtcNow
        );
        Assert.NotNull(proof);
        Assert.Equal(0, fake.Stops + fake.Launches + fake.ForbiddenReads);
    }
}
