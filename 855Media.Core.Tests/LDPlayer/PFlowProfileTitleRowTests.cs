using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowProfileTitleRowTests
{
    private static string Capture() =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures/PFlowScreenTime/ld61-tail-talks-profile-title.xml"
            )
        );

    [Fact]
    public void CapturedTitleDoesNotBecomeAmbiguousBecauseOfCategory() =>
        Assert.Equal("Tail Talks+", PFlowPostingNameVerification.ActiveHeaderName(Capture()));

    [Theory]
    [InlineData("Reel creator")]
    [InlineData("Outdoor & Sporting Goods Company")]
    [InlineData("16 followers")]
    [InlineData("Anything else")]
    [InlineData("")]
    public void SubtitleContentOrAbsenceDoesNotMatter(string subtitle)
    {
        var doc = XDocument.Parse(Capture());
        foreach (
            var n in doc.Descendants("node")
                .Where(n => n.Attribute("text")?.Value == "Digital creator")
        )
            n.SetAttributeValue("text", subtitle);
        Assert.Equal("Tail Talks+", PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData("Bushcraft\nimprovisation", 0.5)]
    [InlineData("Botanik Bushcraft", 1)]
    [InlineData("Z Outdoors+", 1.5)]
    [InlineData("PoolVolution Tips", 2)]
    [InlineData("Dara Aba Rol Sa", 0.75)]
    public void TitleRowSupportsWholeWrappedNamesAndScaledBounds(string name, double scale)
    {
        var doc = XDocument.Parse(Capture());
        foreach (var n in doc.Descendants("node"))
        {
            foreach (
                var a in n.Attributes()
                    .Where(a => a.Name.LocalName is "text" or "content-desc")
                    .ToArray()
            )
                if (a.Value.Trim() == "Tail Talks+")
                    a.Value = name;
            if (
                name.Contains('\n')
                && n.Attribute("text")?.Value == name
                && n.Attribute("bounds")?.Value == "[242,612][486,667]"
            )
                n.SetAttributeValue("bounds", "[200,590][486,667]");
            if (n.Attribute("bounds") is { } b)
                b.Value = System.Text.RegularExpressions.Regex.Replace(
                    b.Value,
                    @"\d+",
                    m => ((int)(int.Parse(m.Value) * scale)).ToString()
                );
        }
        Assert.Equal(name, PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("disabled")]
    [InlineData("truncated")]
    [InlineData("ambiguous")]
    [InlineData("wrong-row")]
    public void MissingOrAmbiguousTitleCannotBeReplacedWithSubtitle(string mode)
    {
        var doc = XDocument.Parse(Capture());
        var titles = doc.Descendants("node")
            .Where(n =>
                n.Attribute("text")?.Value.Trim() == "Tail Talks+"
                && n.Attribute("bounds")?.Value.StartsWith("[242,612]") == true
            )
            .ToArray();
        foreach (var n in titles)
        {
            if (mode == "hidden")
                n.SetAttributeValue("visible-to-user", "false");
            if (mode == "disabled")
                n.SetAttributeValue("enabled", "false");
            if (mode == "truncated")
                n.SetAttributeValue("text", "Tail Tal...");
            if (mode == "wrong-row")
                n.SetAttributeValue("bounds", "[242,730][486,785]");
        }
        if (mode == "ambiguous")
            titles[0]
                .Parent!.Add(
                    new XElement(
                        "node",
                        new XAttribute("package", "com.facebook.katana"),
                        new XAttribute("text", "Another Page"),
                        new XAttribute("bounds", "[10,612][200,667]")
                    )
                );
        Assert.Null(PFlowPostingNameVerification.ActiveHeaderName(doc.ToString()));
    }

    [Fact]
    public async Task CapturedProfileActivatesImmediatelyWithoutMenuSwitchRestartOrGraph()
    {
        var binding = new PFlowAccountBinding(
            "fixture",
            900761,
            "fixture-LD",
            "com.facebook.katana",
            "100000000000001"
        );
        var target = new FacebookPageTarget
        {
            PageId = "200000000000001",
            UserUid = binding.OwnerUid,
            PageName = "Tail Talks+",
            Type = "Page",
            AccountBinding = binding,
        };
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowNameResponseRecoveryTests.Device>();
        var fake = (PFlowNameResponseRecoveryTests.Device)(object)ld;
        fake.Loading = Capture();
        var delays = 0;
        var proof = await PFlowPostingNameVerification.ActivateWithResponseWaitAsync(
            ld,
            binding,
            target,
            [target],
            null,
            default,
            null,
            (_, _) =>
            {
                delays++;
                throw new Exception("Unexpected wait on readable profile");
            },
            () => DateTimeOffset.UtcNow
        );
        Assert.True(proof.UsesDatabaseName);
        Assert.Equal(0, delays + fake.Stops + fake.Launches + fake.ForbiddenReads);
        Assert.True(await proof.SessionUnchangedAsync(ld, null, default));
    }
}
