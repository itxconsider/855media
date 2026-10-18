using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowRootPageNameTests
{
    [Theory]
    [InlineData("first_name\nMetsätalous\nlast_name\nMetsätalous", true, "Metsätalous")]
    [InlineData(
        "\r\nfirst_name\n\nMetsätalous\n\tlast_name\n\nMetsätalous\nname\nMetsätalous Metsätalous",
        true,
        "Metsätalous"
    )]
    [InlineData(
        "first_name\nMetsätalous\nlast_name\nMetsätalous",
        false,
        "Metsätalous Metsätalous"
    )]
    [InlineData(
        "name\nMetsätalous Metsätalous\nfirst_name\nMetsätalous\nlast_name\nMetsätalous",
        true,
        "Metsätalous"
    )]
    [InlineData("first_name\nMarcel\nlast_name\nCamping", true, "Metsätalous Metsätalous")]
    public void OnlyRepeatedLegacyPageFieldsAreCollapsed(string dump, bool page, string expected) =>
        Assert.Equal(
            expected,
            PFlowAccountDiscoveryService.ResolveRootPageName("Metsätalous Metsätalous", dump, page)
        );

    [Fact]
    public void DifferentFullNameAndExplicitRepetitionWithoutDuplicateFieldsArePreserved()
    {
        Assert.Equal(
            "Metsätalous Forestry",
            PFlowAccountDiscoveryService.ResolveRootPageName(
                "Metsätalous Forestry",
                "first_name\nMetsätalous\nlast_name\nMetsätalous",
                true
            )
        );
        Assert.Equal(
            "Bora Bora",
            PFlowAccountDiscoveryService.ResolveRootPageName(
                "Bora Bora",
                "name\nBora Bora\nfirst_name\nBora\nlast_name\n",
                true
            )
        );
    }
}
