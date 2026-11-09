using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowFetchOwnerRecoveryTests
{
    [Fact]
    public async Task WelcomeIsSkippedOnceThenFreshProfileIsReturned()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, WelcomeDevice>();
        var fake = (WelcomeDevice)(object)ld;
        var result = await PFlowNavigationPrompts.DismissAsync(
            ld,
            11,
            FacebookLayoutRegistryTests.Fixture("ld11-profile-welcome"),
            "fixture",
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal(fake.Profile, result);
        Assert.Equal(1, fake.Taps);
    }

    public class WelcomeDevice : DispatchProxy
    {
        public int Taps;
        public string Profile =
            "<hierarchy><node package='com.facebook.katana' text='Michael Petersen' /></hierarchy>";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TapAsync")
            {
                Assert.Equal(192, args![1]);
                Assert.Equal(1212, args[2]);
                Taps++;
                return Task.FromResult(true);
            }
            if (method.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(Profile);
            throw new NotSupportedException(method.Name);
        }
    }

    [Fact]
    public void CapturedWelcomeAllowsOnlyUniqueUsableSkip()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld11-profile-welcome");
        Assert.Equal((192, 1212), PFlowNavigationPrompts.ProfileWelcomeSkip(xml));
        Assert.Null(
            PFlowNavigationPrompts.ProfileWelcomeSkip(
                xml.Replace("Welcome to your profile", "Other screen")
            )
        );
        Assert.Null(
            PFlowNavigationPrompts.ProfileWelcomeSkip(
                xml.Replace("com.facebook.katana", "other.app")
            )
        );
        var doc = XDocument.Parse(xml);
        var skip = doc.Descendants("node")
            .Single(n => n.Attribute("content-desc")?.Value == "Skip");
        skip.SetAttributeValue("enabled", "false");
        Assert.Null(PFlowNavigationPrompts.ProfileWelcomeSkip(doc.ToString()));
    }

    [Fact]
    public void CapturedPageDrawerHasExactPersonalShortcut()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld3-owner-shortcut");
        Assert.Null(PFlowPostingNameVerification.PersonalShortcut(xml, "Harch Jing"));
        var shortcut = PFlowPostingNameVerification.PersonalShortcut(
            xml,
            "Harch Jing",
            requireUsable: false
        );
        Assert.NotNull(shortcut);
        var doc = XDocument.Parse(xml);
        foreach (
            var n in doc.Descendants("node").Where(n => n.Attribute("enabled")?.Value == "false")
        )
            n.SetAttributeValue("enabled", "true");
        Assert.NotNull(PFlowPostingNameVerification.PersonalShortcut(doc.ToString(), "Harch Jing"));
        Assert.Null(PFlowPostingNameVerification.PersonalShortcut(xml, "Other owner"));
        Assert.Null(
            PFlowPostingNameVerification.PersonalShortcut(
                xml.Replace("enabled=\"true\"", "enabled=\"false\""),
                "Harch Jing"
            )
        );
    }
}
