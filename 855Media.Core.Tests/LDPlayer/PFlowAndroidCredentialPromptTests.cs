using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowAndroidCredentialPromptTests
{
    // Labels and nested clickable Close structure observed on LD-1; no real credentials.
    public static string Prompt(string title = "Use your saved password for Facebook")
    {
        XElement Node(
            string text = "",
            string desc = "",
            bool clickable = false,
            string bounds = "[0,0][720,1280]"
        ) =>
            new(
                "node",
                new XAttribute("package", "com.google.android.gms"),
                new XAttribute("class", "android.view.View"),
                new XAttribute("enabled", "true"),
                new XAttribute("clickable", clickable ? "true" : "false"),
                new XAttribute("text", text),
                new XAttribute("content-desc", desc),
                new XAttribute("bounds", bounds)
            );
        var close = Node(clickable: true, bounds: "[592,698][688,794]");
        close.Add(Node(desc: "Close", bounds: "[624,730][656,762]"));
        return new XElement(
            "hierarchy",
            Node(text: "Facebook"),
            Node(text: title),
            close,
            Node(text: "fixture-login-identifier"),
            Node(text: "fixture-password"),
            Node(text: "Continue", clickable: true, bounds: "[511,1176][688,1272]")
        ).ToString();
    }

    [Theory]
    [InlineData("Use your saved password for Facebook")]
    [InlineData("Save password to sign in to Facebook?")]
    public void BothObservedPromptsSelectCloseRatherThanContinue(string title) =>
        Assert.Equal((640, 746), PFlowAndroidCredentialPrompt.CloseControl(Prompt(title)));

    [Fact]
    public void UnknownTitleOtherAppAndMissingFacebookMarkerAreRejected()
    {
        Assert.Null(PFlowAndroidCredentialPrompt.CloseControl(Prompt("Unrecognized prompt")));
        Assert.Null(
            PFlowAndroidCredentialPrompt.CloseControl(
                Prompt().Replace("com.google.android.gms", "com.other.app")
            )
        );
        Assert.Null(
            PFlowAndroidCredentialPrompt.CloseControl(
                Prompt().Replace("text=\"Facebook\"", "text=\"Other\"")
            )
        );
        Assert.Null(PFlowAndroidCredentialPrompt.CloseControl("malformed"));
    }

    [Fact]
    public void DisabledCloseAndMissingClickableAncestorAreRejected()
    {
        var doc = XDocument.Parse(Prompt());
        var close = doc.Descendants("node")
            .Single(n => (string?)n.Attribute("content-desc") == "Close");
        close.SetAttributeValue("enabled", "false");
        Assert.Null(PFlowAndroidCredentialPrompt.CloseControl(doc.ToString()));
        close.SetAttributeValue("enabled", "true");
        close.Parent!.SetAttributeValue("clickable", "false");
        Assert.Null(PFlowAndroidCredentialPrompt.CloseControl(doc.ToString()));
    }

    [Fact]
    public void MultipleCloseTargetsAreRejected()
    {
        var doc = XDocument.Parse(Prompt());
        var other = new XElement(
            doc.Descendants("node")
                .Single(n => (string?)n.Attribute("content-desc") == "Close")
                .Parent!
        );
        other.SetAttributeValue("bounds", "[0,200][96,296]");
        doc.Root!.Add(other);
        Assert.Null(PFlowAndroidCredentialPrompt.CloseControl(doc.ToString()));
    }

    [Fact]
    public void DiagnosticCaptureRedactsAndroidCredentialValues()
    {
        var redacted = PFlowUiPrivacy.RedactForDiagnostics(Prompt());
        Assert.DoesNotContain("fixture-login-identifier", redacted);
        Assert.DoesNotContain("fixture-password", redacted);
    }
}
