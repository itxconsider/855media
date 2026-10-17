using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookNameDetailsLayoutTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NameDetailsSheetOnlyExposesTransparencyWithOrWithoutVerificationOffer(
        bool promotion
    )
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("profile-details-verified-offer-screenshot")
        );
        if (!promotion)
            doc.Descendants("node")
                .Single(n => n.Attribute("text")?.Value == "Get verified with Meta One")
                .Parent!.Remove();
        var match = FacebookLayoutRegistry.Match(
            doc.ToString(),
            FacebookScreenKind.ProfileDetails,
            new("Nick Parlet")
        );
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(
            promotion ? "profile.details-verified-offer.v1" : "profile.details-sheet.v1",
            match.VariantId
        );
        var control = Assert.Single(match.Controls);
        Assert.Equal(FacebookControlRole.Transparency, control.Role);
        Assert.Equal((156, 441), (control.X, control.Y));
        Assert.False(
            FacebookLayoutRegistry
                .Match(doc.ToString(), FacebookScreenKind.ProfileDetails, new("Other Page"))
                .IsMatch
        );
        Assert.False(PFlowProfileDates.HasFullDate("Created October 2026"));
    }

    [Fact]
    public void DuplicateTransparencyControlsRemainAmbiguousAndPromotionCloseIsIgnored()
    {
        var doc = XDocument.Parse(
            FacebookLayoutRegistryTests.Fixture("profile-details-verified-offer-screenshot")
        );
        var entry = doc.Descendants("node")
            .Single(n => n.Attribute("text")?.Value == "Transparency and privacy policy");
        var duplicate = new XElement(entry);
        duplicate.SetAttributeValue("bounds", "[14,380][299,420]");
        entry.Parent!.Add(duplicate);
        var match = FacebookLayoutRegistry.Match(
            doc.ToString(),
            FacebookScreenKind.ProfileDetails,
            new("Nick Parlet")
        );
        Assert.True(match.IsMatch);
        Assert.Null(match.Control(FacebookControlRole.Transparency));
        Assert.Null(match.Control(FacebookControlRole.Dismiss));
        Assert.Contains("Transparency:ambiguous", match.MissingControls);
    }
}
