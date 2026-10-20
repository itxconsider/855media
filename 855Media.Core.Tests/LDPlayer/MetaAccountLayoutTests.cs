using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class MetaAccountLayoutTests
{
    [Theory]
    [InlineData(1, "settings", FacebookScreenKind.MetaAccountSettings)]
    [InlineData(2, "settings", FacebookScreenKind.MetaAccountSettings)]
    [InlineData(1, "home", FacebookScreenKind.MetaAccount)]
    [InlineData(2, "home", FacebookScreenKind.MetaAccount)]
    [InlineData(1, "information", FacebookScreenKind.PersonalInformationPermissions)]
    [InlineData(2, "information", FacebookScreenKind.PersonalInformationPermissions)]
    [InlineData(1, "birthday", FacebookScreenKind.MetaBirthdayDetails)]
    [InlineData(2, "birthday", FacebookScreenKind.MetaBirthdayDetails)]
    public void SanitizedLiveCapturesMatchRegistryAndReadOnlyDate(
        int ldIndex,
        string screen,
        FacebookScreenKind kind
    )
    {
        var xml = FacebookLayoutRegistryTests.Fixture($"ld{ldIndex}-meta-account-{screen}");
        var match = FacebookLayoutRegistry.Match(xml, kind);
        Assert.True(match.IsMatch, match.Diagnostic);
        if (kind == FacebookScreenKind.MetaAccountSettings)
            Assert.NotNull(match.Control(FacebookControlRole.OpenMetaAccount));
        if (kind == FacebookScreenKind.MetaAccount)
            Assert.NotNull(match.Control(FacebookControlRole.PersonalInformation));
        if (
            kind
            is FacebookScreenKind.PersonalInformationPermissions
                or FacebookScreenKind.MetaBirthdayDetails
        )
        {
            Assert.Equal("March 14, 1992", PFlowPersonalBirthdayReader.ReadDate(xml, true));
            Assert.Empty(match.Controls);
        }
    }

    private static XElement Node(string text, int y, bool click = false) =>
        new(
            "node",
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute("text", text),
            new XAttribute("enabled", "true"),
            new XAttribute("clickable", click ? "true" : "false"),
            new XAttribute("bounds", $"[20,{y}][680,{y + 40}]")
        );

    private static string Screen(params XElement[] nodes) =>
        new XElement(
            "hierarchy",
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("bounds", "[0,0][720,1280]"),
                nodes.Append(Node("Back", 20, true))
            )
        ).ToString();

    internal static string Settings() =>
        Screen(
            Node("Settings & privacy", 80),
            Node("Your account", 170),
            Node("Meta Account", 230, true),
            Node("Subscriptions", 340),
            Node("Tools and resources", 500),
            new XElement(
                "node",
                new XAttribute("package", "com.facebook.katana"),
                new XAttribute("class", "android.widget.EditText"),
                new XAttribute("text", "Search settings")
            )
        );

    internal static string Home() =>
        Screen(
            Node("Meta Account", 100),
            Node("Login and security", 300, true),
            Node("Cross-profile experiences", 400, true),
            Node("Your information and permissions", 500, true)
        );

    internal static string Information()
    {
        var birthday = Node("", 220);
        birthday.SetAttributeValue("bounds", "[15,200][700,320]");
        birthday.Add(Node("Birthday", 210), Node("March 14, 1992", 260));
        return Screen(
            Node("Your information and permissions", 90),
            birthday,
            Node("Export your information", 400, true),
            Node("Access your information", 500, true),
            Node("Search history", 600, true)
        );
    }

    internal static string Birthday(bool ageConfirmation)
    {
        var row = Node("", 250);
        row.SetAttributeValue("bounds", "[15,220][700,320]");
        row.Add(Node("March 14, 1992", 240), Node("Edit", 240, true));
        return Screen(
            Node("Birthday", 100),
            row,
            Node("Who can see your birthday", 450, true),
            Node(ageConfirmation ? "Confirm your age" : "More options", 380, ageConfirmation)
        );
    }

    [Fact]
    public void RegistryRecognizesNewRouteWithOnlyReadOnlyNavigationActions()
    {
        Assert.True(
            FacebookLayoutRegistry.Match(Settings(), FacebookScreenKind.MetaAccountSettings).IsMatch
        );
        Assert.NotNull(
            FacebookLayoutRegistry
                .Match(Settings(), FacebookScreenKind.MetaAccountSettings)
                .Control(FacebookControlRole.OpenMetaAccount)
        );
        Assert.NotNull(
            FacebookLayoutRegistry
                .Match(Home(), FacebookScreenKind.MetaAccount)
                .Control(FacebookControlRole.PersonalInformation)
        );
        Assert.True(
            FacebookLayoutRegistry
                .Match(Information(), FacebookScreenKind.PersonalInformationPermissions)
                .IsMatch
        );
        Assert.Equal("March 14, 1992", PFlowPersonalBirthdayReader.ReadDate(Information(), true));
        Assert.Empty(PFlowPersonalBirthdayReader.ReadDate(Information(), false));
        foreach (var age in new[] { true, false })
        {
            var match = FacebookLayoutRegistry.Match(
                Birthday(age),
                FacebookScreenKind.MetaBirthdayDetails
            );
            Assert.True(match.IsMatch, match.Diagnostic);
            Assert.Empty(match.Controls);
            Assert.Equal(
                "March 14, 1992",
                PFlowPersonalBirthdayReader.ReadDate(Birthday(age), true)
            );
        }
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("password")]
    [InlineData("missing")]
    public void UnrelatedOrEditableInformationDoesNotSupplyBirthday(string change)
    {
        var xml = Information();
        xml = change switch
        {
            "foreign" => xml.Replace("com.facebook.katana", "another.app"),
            "input" => xml.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
            ),
            "password" => xml.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' password='true'/></hierarchy>"
            ),
            _ => xml.Replace("Export your information", "Different action"),
        };
        Assert.False(
            FacebookLayoutRegistry
                .Match(xml, FacebookScreenKind.PersonalInformationPermissions)
                .IsMatch
        );
        Assert.Empty(PFlowPersonalBirthdayReader.ReadDate(xml, true));
    }
}
