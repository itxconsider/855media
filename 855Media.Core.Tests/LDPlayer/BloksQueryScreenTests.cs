using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class BloksQueryScreenTests
{
    [Fact]
    public void ScreenQueryUri_PreservesServerParametersWithoutAddingQueryKeys()
    {
        var uri = new Uri(
            BloksConstants.CreateBloksScreenQueryUri(
                "com.bloks.test&app_id=other",
                new Dictionary<string, string>
                {
                    ["page_uid"] = "61594987617831",
                    ["delegate_page_id"] = "1471510549372558",
                    ["entry_point"] = "profile + dashboard & analytics",
                }
            )
        );
        Assert.Equal("bloks_screen_query", uri.Host);
        var query = uri
            .Query.TrimStart('?')
            .Split('&')
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
        Assert.Equal(2, query.Count);
        Assert.Equal("com.bloks.test&app_id=other", query["app_id"]);
        using var payload = JsonDocument.Parse(query["bloks_screen_params"]);
        var server = payload.RootElement.GetProperty("server_params");
        Assert.Equal("61594987617831", server.GetProperty("page_uid").GetString());
        Assert.Equal("1471510549372558", server.GetProperty("delegate_page_id").GetString());
        Assert.Equal(
            "profile + dashboard & analytics",
            server.GetProperty("entry_point").GetString()
        );
        Assert.Empty(payload.RootElement.GetProperty("client_input_params").EnumerateObject());
    }

    [Theory]
    [InlineData(BloksConstants.FbBloksNativeShellActivity)]
    [InlineData(BloksConstants.BloksScreenQueryActivity)]
    [InlineData(BloksConstants.BloksScreenQueryTransparentActivity)]
    public void InstalledFacebookActivity_IsRecognized(string activity) =>
        Assert.True(BloksConstants.IsKnownBloksActivity("com.facebook.katana/" + activity));

    [Fact]
    public void DemoLauncher_IsRejectedEvenThoughBloksHostIsActive()
    {
        var screen = Screen(
            "Bloks Native Shell",
            "Open Screen Query",
            "Prebundled Screen Query Example"
        );
        Assert.Equal(BloksQueryScreenState.DemoShell, BloksQueryScreenValidator.Inspect(screen));
    }

    [Fact]
    public void QueryError_IsRejected()
    {
        var screen = Screen("Something went wrong", "Try again");
        Assert.Equal(BloksQueryScreenState.Error, BloksQueryScreenValidator.Inspect(screen));
    }

    [Fact]
    public void FailedNavigation_RemainsUnchangedWhenStatusClockMoves()
    {
        var previous = Screen("2:38", "Accounts Center", "Personal details");
        var current = Screen("2:39", "Accounts Center", "Personal details");
        Assert.Equal(
            BloksQueryScreenState.Unchanged,
            BloksQueryScreenValidator.Inspect(current, previous)
        );
    }

    [Fact]
    public void NewlyLoadedContent_IsReadyForCallerIdentityValidation()
    {
        var previous = Screen("Menu", "Settings");
        var current = Screen("Personal details", "Birthday", "March 23, 1999");
        Assert.Equal(
            BloksQueryScreenState.Ready,
            BloksQueryScreenValidator.Inspect(current, previous)
        );
    }

    [Theory]
    [InlineData("Loading...")]
    [InlineData("Loading…")]
    [InlineData("Please wait")]
    public void TitleAndLoadingPlaceholder_AreNotReady(string placeholder)
    {
        var screen = Screen("Back", "Accounts Center", placeholder);
        Assert.Equal(
            BloksQueryScreenState.AwaitingContent,
            BloksQueryScreenValidator.Inspect(screen)
        );
    }

    [Fact]
    public void ToolbarAndTitleAlone_AreNotReady()
    {
        var screen = Screen("Navigate up", "Personal details", "More options");
        Assert.Equal(
            BloksQueryScreenState.AwaitingContent,
            BloksQueryScreenValidator.Inspect(screen)
        );
    }

    private static BloksScreenInfo Screen(params string[] labels)
    {
        var xml = new XElement(
            "hierarchy",
            labels.Select(label => new XElement(
                "node",
                new XAttribute("text", label),
                new XAttribute("class", "android.widget.TextView")
            ))
        );
        return AtxAgentClient.ParseBloksHierarchy(
            xml.ToString(),
            BloksConstants.BloksScreenQueryActivity
        );
    }
}
