using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookOnboardingLayoutTests
{
    private static string Fixture(string name) => FacebookLayoutRegistryTests.Fixture(name);

    [Theory]
    [InlineData(
        "onboarding-contacts-access-screenshot",
        PFlowLoginScreenKind.ContactsAccessIntro,
        FacebookControlRole.Skip,
        "authentication.contactsaccessintro.v1"
    )]
    [InlineData(
        "onboarding-language-suggestion-screenshot",
        PFlowLoginScreenKind.LanguageSuggestion,
        FacebookControlRole.ContinueInEnglish,
        "authentication.languagesuggestion.v1"
    )]
    public void ScreenshotPromptsHaveOneVariantAndTheirSpecificNavigationControl(
        string file,
        PFlowLoginScreenKind kind,
        FacebookControlRole role,
        string variant
    )
    {
        var xml = Fixture(file);
        Assert.Equal(kind, PFlowLoginScreen.Classify(xml));
        Assert.False(PFlowLoginScreen.RequiresAuthentication(kind));
        var match = FacebookLayoutRegistry.Match(xml);
        Assert.True(match.IsMatch, match.Diagnostic);
        Assert.Equal(FacebookScreenKind.Authentication, match.Screen);
        Assert.Equal(variant, match.VariantId);
        Assert.NotNull(match.Control(role));
        Assert.Single(match.Controls);
        Assert.Null(match.Control(FacebookControlRole.Continue));
        Assert.Null(match.Control(FacebookControlRole.Publish));
        Assert.False(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile).IsMatch);
    }

    [Theory]
    [InlineData(
        "onboarding-contacts-access-screenshot",
        "Allow contacts access to find people to follow"
    )]
    [InlineData("onboarding-language-suggestion-screenshot", "设置语言")]
    public void ButtonsAloneForeignAppsAndEditableScreensDoNotMatch(string file, string heading)
    {
        var xml = Fixture(file);
        var kind = PFlowLoginScreen.Classify(xml);
        Assert.NotEqual(kind, PFlowLoginScreen.Classify(xml.Replace(heading, "Other screen")));
        Assert.NotEqual(
            kind,
            PFlowLoginScreen.Classify(xml.Replace("com.facebook.katana", "com.other.app"))
        );
        var doc = XDocument.Parse(xml);
        doc.Root!.Element("node")!
            .Add(
                new XElement(
                    "node",
                    new XAttribute("package", "com.facebook.katana"),
                    new XAttribute("class", "android.widget.EditText")
                )
            );
        Assert.NotEqual(kind, PFlowLoginScreen.Classify(doc.ToString()));
        doc = XDocument.Parse(xml);
        doc.Root!.Add(
            new XElement("node", new XAttribute("package", "com.android.permissioncontroller"))
        );
        Assert.NotEqual(kind, PFlowLoginScreen.Classify(doc.ToString()));
    }

    [Theory]
    [InlineData("onboarding-contacts-access-screenshot", "Skip", FacebookControlRole.Skip)]
    [InlineData(
        "onboarding-language-suggestion-screenshot",
        "Continue in English (US)",
        FacebookControlRole.ContinueInEnglish
    )]
    public void DisabledHiddenOffscreenAndDuplicateControlsAreUnavailable(
        string file,
        string label,
        FacebookControlRole role
    )
    {
        foreach (var state in new[] { "disabled", "hidden", "offscreen", "duplicate" })
        {
            var doc = XDocument.Parse(Fixture(file));
            var button = doc.Descendants("node").Single(n => (string?)n.Attribute("text") == label);
            switch (state)
            {
                case "disabled":
                    button.SetAttributeValue("enabled", "false");
                    button.SetAttributeValue("clickable", "false");
                    break;
                case "hidden":
                    button.SetAttributeValue("visible-to-user", "false");
                    break;
                case "offscreen":
                    button.SetAttributeValue("bounds", "[7,600][312,638]");
                    break;
                case "duplicate":
                    var duplicate = new XElement(button);
                    duplicate.SetAttributeValue("bounds", "[7,400][312,438]");
                    button.Parent!.Add(duplicate);
                    break;
            }
            Assert.Null(FacebookLayoutRegistry.Match(doc.ToString()).Control(role));
        }
    }

    [Fact]
    public void WrappedAndFullWidthChinesePromptLabelsRemainRecognizable()
    {
        var xml = Fixture("onboarding-language-suggestion-screenshot")
            .Replace("试用中文(简体) 版 Facebook", "试用中文（简体）版&#10;Facebook");
        Assert.Equal(PFlowLoginScreenKind.LanguageSuggestion, PFlowLoginScreen.Classify(xml));
    }
}
