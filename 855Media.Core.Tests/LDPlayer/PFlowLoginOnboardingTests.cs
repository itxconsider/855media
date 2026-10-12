using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowLoginOnboardingTests : IDisposable
{
    private const int Index = 900188;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "pflow-onboarding-" + Guid.NewGuid().ToString("N")
    );
    private PFlowAccountBinding Binding =>
        new(
            PFlowDeviceRouting.ReadIdentity(_root, Index),
            Index,
            "fixture-LD",
            "com.facebook.katana",
            "123"
        );

    public PFlowLoginOnboardingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vms", "config"));
        Directory.CreateDirectory(Path.Combine(_root, "vms", "leidian" + Index));
        File.WriteAllText(
            Path.Combine(_root, "vms", "config", "leidian" + Index + ".config"),
            "{\"propertySettings.phoneAndroidId\":\"fixture\"}"
        );
        File.WriteAllText(Path.Combine(_root, "vms", "leidian" + Index, "data.vmdk"), "fixture");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static XElement Node(
        string text,
        bool click = false,
        string package = "com.facebook.katana",
        string bounds = "[0,0][200,60]"
    ) =>
        new(
            "node",
            new XAttribute("text", text),
            new XAttribute("content-desc", ""),
            new XAttribute("package", package),
            new XAttribute("enabled", "true"),
            new XAttribute("clickable", click ? "true" : "false"),
            new XAttribute("bounds", bounds)
        );

    private static string Xml(params XElement[] nodes) =>
        new XElement("hierarchy", nodes).ToString();

    private static string Intro() =>
        Xml(
            Node("To use Location Services, allow Facebook to access your location"),
            Node("How you can use Location Services"),
            Node("Skip", true, bounds: "[508,77][688,123]"),
            Node("Continue", true, bounds: "[24,1199][696,1279]")
        );

    private static string Permission(
        string app = "Facebook",
        string capability = "location",
        string package = "com.android.packageinstaller"
    )
    {
        var allow = Node("ALLOW", true, package, "[440,700][680,800]");
        allow.SetAttributeValue("resource-id", package + ":id/permission_allow_button");
        return Xml(
            Node("Allow " + app + " to access this device's " + capability + "?", package: package),
            allow
        );
    }

    private static string SaveInfo() =>
        Xml(
            Node("Save your login info?"),
            Node("NOT NOW", true, bounds: "[300,700][450,800]"),
            Node("SAVE", true, bounds: "[500,700][650,800]")
        );

    [Fact]
    public void LocationIntroRequiresTheSpecificTitleAndBothControls()
    {
        Assert.Equal(
            PFlowLoginScreenKind.LocationServicesIntro,
            PFlowLoginScreen.Classify(Intro())
        );
        Assert.NotEqual(
            PFlowLoginScreenKind.LocationServicesIntro,
            PFlowLoginScreen.Classify(Xml(Node("Continue", true)))
        );
        Assert.NotEqual(
            PFlowLoginScreenKind.LocationServicesIntro,
            PFlowLoginScreen.Classify(Intro().Replace("Location Services", "Other Services"))
        );
        Assert.False(
            PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreenKind.LocationServicesIntro)
        );
    }

    [Theory]
    [InlineData("com.android.packageinstaller")]
    [InlineData("com.google.android.packageinstaller")]
    public void OnlyFacebookLocationPermissionIsAccepted(string package) =>
        Assert.Equal(
            (560, 750),
            PFlowNativeLoginOnboarding.LocationPermissionControl(Permission(package: package))
        );

    [Theory]
    [InlineData("Other app", "location", "com.android.packageinstaller")]
    [InlineData("Facebook", "contacts", "com.android.packageinstaller")]
    [InlineData("Facebook", "camera", "com.android.packageinstaller")]
    [InlineData("Facebook", "location", "com.other.app")]
    public void DifferentAppsAndOtherPermissionsAreRejected(
        string app,
        string capability,
        string package
    ) =>
        Assert.Null(
            PFlowNativeLoginOnboarding.LocationPermissionControl(
                Permission(app, capability, package)
            )
        );

    [Fact]
    public void DisabledOrAmbiguousPermissionButtonsAreRejected()
    {
        var doc = XDocument.Parse(Permission());
        var allow = doc.Descendants("node").Single(n => (string?)n.Attribute("text") == "ALLOW");
        allow.SetAttributeValue("enabled", "false");
        Assert.Null(PFlowNativeLoginOnboarding.LocationPermissionControl(doc.ToString()));
        allow.SetAttributeValue("enabled", "true");
        var other = new XElement(allow);
        other.SetAttributeValue("bounds", "[0,100][200,160]");
        doc.Root!.Add(other);
        Assert.Null(PFlowNativeLoginOnboarding.LocationPermissionControl(doc.ToString()));
        Assert.Null(PFlowNativeLoginOnboarding.LocationPermissionControl("malformed"));
    }

    [Fact]
    public async Task ContinueLocationThenAllowThenDeclineSavingCredentials()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        var result = await new PFlowNativeLoginOnboarding(
            ld,
            _root,
            (_, _) => Task.CompletedTask
        ).CompleteLocationAsync(Binding, default);
        Assert.Equal(
            "complete",
            XDocument.Parse(result).Descendants("node").Single().Attribute("text")!.Value
        );
        Assert.Equal(new[] { (360, 1239), (560, 750), (375, 750) }, fake.Taps);
        Assert.All(fake.Indices, i => Assert.Equal(Index, i));
    }

    [Fact]
    public async Task ChangedBindingCannotContinueOrGrantPermission()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeLoginOnboarding(
                ld,
                _root,
                (_, _) => Task.CompletedTask
            ).CompleteLocationAsync(Binding with { InstanceIdentity = "changed" }, default)
        );
        Assert.Empty(fake.Taps);
    }

    [Fact]
    public async Task UnexpectedPermissionStopsAfterContinueWithoutGrantingIt()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.PermissionXml = Permission(capability: "contacts");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowNativeLoginOnboarding(
                ld,
                _root,
                (_, _) => Task.CompletedTask
            ).CompleteLocationAsync(Binding, default)
        );
        Assert.Single(fake.Taps);
    }

    public class Device : DispatchProxy
    {
        public string PermissionXml = Permission();
        public List<(int X, int Y)> Taps = [];
        public List<int> Indices = [];
        private int _stage;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (args?.FirstOrDefault() is int index)
                Indices.Add(index);
            return method!.Name switch
            {
                "GetInstancesAsync" => Task.FromResult<IReadOnlyList<LDInstanceInfo>>(
                    new[]
                    {
                        new LDInstanceInfo
                        {
                            Index = Index,
                            Name = "fixture-LD",
                            IsAndroidStarted = true,
                        },
                    }
                ),
                "GetCurrentFocusedActivityAsync" => Task.FromResult<string?>(
                    _stage == 1
                        ? "com.android.packageinstaller/com.android.packageinstaller.permission.ui.GrantPermissionsActivity"
                        : "com.facebook.katana/ProfileActivity"
                ),
                "DumpUiHierarchyAsync" => Task.FromResult<string?>(
                    _stage switch
                    {
                        0 => Intro(),
                        1 => PermissionXml,
                        2 => SaveInfo(),
                        _ => Xml(Node("complete")),
                    }
                ),
                "TapAsync" => Tap(args!),
                _ => throw new InvalidOperationException("Unexpected operation: " + method.Name),
            };
        }

        private Task<bool> Tap(object?[] args)
        {
            Taps.Add(((int)args[1]!, (int)args[2]!));
            _stage++;
            return Task.FromResult(true);
        }
    }
}
