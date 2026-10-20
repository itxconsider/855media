using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowMediaAccessTests
{
    private const string Facebook = "com.facebook.katana";
    private const string SystemPackage = "com.android.packageinstaller";

    private static XElement Node(
        string text,
        bool click = false,
        string package = Facebook,
        string? id = null
    ) =>
        new(
            "node",
            new XAttribute("text", text),
            new XAttribute("package", package),
            new XAttribute("enabled", "true"),
            new XAttribute("clickable", click ? "true" : "false"),
            new XAttribute("bounds", "[20,100][200,160]"),
            new XAttribute("resource-id", id ?? "")
        );

    private static string Xml(params XElement[] nodes) =>
        new XElement("hierarchy", nodes).ToString();

    internal static string Intro() =>
        Xml(
            Node("Create reel"),
            Node("Allow access to your camera roll"),
            Node("Allow access", true)
        );

    internal static string Storage() =>
        Xml(
            Node("Create reel"),
            Node("Allow access to your camera roll"),
            Node("Allow access", true),
            Node("Allow Facebook to use your phone’s storage?"),
            Node("Deny", true),
            Node("Allow", true)
        );

    internal static string Permission(
        string message = "Allow Facebook to access photos, media, and files on your device?"
    ) =>
        Xml(
            Node(message, package: SystemPackage),
            Node("ALLOW", true, SystemPackage, SystemPackage + ":id/permission_allow_button")
        );

    [Fact]
    public void MatchesBothFacebookScreensAndOnlyStorageAndroidPermission()
    {
        Assert.Equal(
            "android-media",
            PFlowMediaAccess
                .FindControl(
                    Permission(
                        "Allow Facebook to\naccess photos, media,\nand files on your\ndevice?"
                    ),
                    Facebook
                )
                ?.Kind
        );
        Assert.Equal("camera-roll", PFlowMediaAccess.FindControl(Intro(), Facebook)?.Kind);
        Assert.Equal("facebook-storage", PFlowMediaAccess.FindControl(Storage(), Facebook)?.Kind);
        Assert.Equal("android-media", PFlowMediaAccess.FindControl(Permission(), Facebook)?.Kind);
        Assert.Null(
            PFlowMediaAccess.FindControl(
                Permission("Allow Facebook to access your location?"),
                Facebook
            )
        );
        Assert.Null(
            PFlowMediaAccess.FindControl(Permission().Replace("Facebook", "Another app"), Facebook)
        );
        Assert.Null(PFlowMediaAccess.FindControl(Intro(), "another.package"));
    }

    [Fact]
    public void DoesNotTapBackgroundIntroBehindAnotherPermission()
    {
        var overStorage = new XElement(
            "hierarchy",
            XElement.Parse(Storage()).Elements(),
            XElement.Parse(Permission()).Elements()
        ).ToString();
        Assert.Equal("android-media", PFlowMediaAccess.FindControl(overStorage, Facebook)?.Kind);
        var xml = new XElement(
            "hierarchy",
            XElement.Parse(Intro()).Elements(),
            XElement
                .Parse(Permission("Allow Facebook to take pictures and record video?"))
                .Elements()
        ).ToString();
        Assert.Null(PFlowMediaAccess.FindControl(xml, Facebook));
        Assert.Null(
            PFlowMediaAccess.FindControl(Storage().Replace("phone’s storage", "camera"), Facebook)
        );
    }

    [Fact]
    public void HiddenDisabledAndAmbiguousControlsAreRejected()
    {
        Assert.Null(
            PFlowMediaAccess.FindControl(
                Intro().Replace("enabled=\"true\"", "enabled=\"false\""),
                Facebook
            )
        );
        var hidden = new XElement(
            "hierarchy",
            new XElement(
                "node",
                new XAttribute("visible-to-user", "false"),
                XElement.Parse(Intro()).Elements()
            )
        ).ToString();
        Assert.Null(PFlowMediaAccess.FindControl(hidden, Facebook));
        var duplicate = XElement.Parse(Intro());
        var other = Node("Allow access", true);
        other.SetAttributeValue("bounds", "[20,200][200,260]");
        duplicate.Add(other);
        Assert.Null(PFlowMediaAccess.FindControl(duplicate.ToString(), Facebook));
    }

    [Fact]
    public async Task CompletesFirstUseSequenceWithoutTappingAnyMedia()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PermissionDevice>();
        var device = (PermissionDevice)(object)ld;
        device.Screens = new Queue<string>(new[] { Storage(), Permission(), "<hierarchy/>" });
        Assert.Equal(
            "<hierarchy/>",
            await PFlowMediaAccess.CompleteAsync(
                ld,
                5,
                Facebook,
                null,
                Intro(),
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(3, device.Taps);
    }

    [Fact]
    public async Task StuckPromptFailsWithoutRepeatedTaps()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PermissionDevice>();
        var device = (PermissionDevice)(object)ld;
        device.Fallback = Intro();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowMediaAccess.CompleteAsync(
                ld,
                5,
                Facebook,
                null,
                Intro(),
                default,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(1, device.Taps);
    }

    public class PermissionDevice : DispatchProxy
    {
        public Queue<string> Screens = new();
        public string Fallback = "<hierarchy/>";
        public int Taps;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TapAsync")
            {
                Taps++;
                return Task.FromResult(true);
            }
            if (method.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(Screens.Count > 0 ? Screens.Dequeue() : Fallback);
            throw new NotSupportedException(method.Name);
        }
    }
}
