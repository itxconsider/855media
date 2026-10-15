using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookNavigationLabelsTests
{
    [Theory]
    [InlineData("See more")]
    [InlineData("See all")]
    [InlineData(" SEE ALL ")]
    [InlineData("See\u00a0more")]
    public void ExpansionVariantsAreActionsRatherThanPageNames(string label)
    {
        Assert.True(FacebookNavigationLabels.IsSectionExpansion(label));
        Assert.True(LDPlayerService.IsIgnoredSwitcherHeaderOrAction(label));
        Assert.True(PFlowAccountDiscoveryService.IsIgnoredSwitcherHeaderOrAction(label));
        Assert.True(PFlowPersonaSwitcherService.IsIgnoredSwitcherHeaderOrAction(label));
    }

    [Theory]
    [InlineData("See all photos")]
    [InlineData("See more options for this post")]
    [InlineData("See all about info")]
    [InlineData("See all profiles")]
    [InlineData("Target Page")]
    [InlineData(null)]
    public void OtherDestinationsAreNotSectionExpansionAliases(string? label) =>
        Assert.False(FacebookNavigationLabels.IsSectionExpansion(label));
}
