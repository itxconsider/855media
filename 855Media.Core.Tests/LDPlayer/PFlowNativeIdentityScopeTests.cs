using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowNativeIdentityScopeTests
{
    private const string Owner = "100000000000001";
    private const string Page = "100000000000002";
    private const string Token = "EAAFixtureIdentityOnly12345";
    private static PFlowAccountBinding Binding =>
        new("fixture-device", 12, "LD-12", "com.facebook.katana", Owner);
    private static FacebookPageTarget Target =>
        new()
        {
            PageId = Page,
            UserUid = Owner,
            PageName = "Z Outdoors+",
            Type = "Page",
        };
    private static PFlowGraphIdentity Graph =>
        new(Page, "Z Outdoors+", Owner, null)
        {
            AuthenticationFingerprint = PFlowGraphIdentityReader.Fingerprint(Token, Owner),
        };

    private static PFlowNativeIdentityScope Scope() =>
        PFlowNativeIdentityScope.FromVerifiedGraph(Binding, "123", Target, Graph)!;

    private static (ILDPlayerService Ld, Device Device) Fake()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("token", false)]
    [InlineData("process", false)]
    [InlineData("owner", false)]
    [InlineData("target", false)]
    [InlineData("foreground", false)]
    public async Task ContinuationChecksSessionWithoutProfileTaps(string change, bool expected)
    {
        var (ld, device) = Fake();
        var scope = Scope();
        var job = new PFlowJob
        {
            DeviceIndex = 12,
            AccountBinding = Binding,
            OwnerUid = Owner,
            TargetId = Page,
            TargetName = "Z Outdoors+",
            AppPackage = Binding.AppPackage,
        };
        if (change == "token")
            device.AuthToken += "changed";
        if (change == "process")
            device.Pid = "999";
        if (change == "owner")
            device.OwnerUid = "100000000000099";
        if (change == "target")
            job.TargetId = "100000000000099";
        if (change == "foreground")
            device.Activity = "other.app/Activity";
        var feed = FacebookLayoutRegistryTests.Fixture("ld2-feed-after-kc-waterheaters-confirmed");
        Assert.Equal(expected, await scope.VerifyContinuationAsync(job, feed, ld, null, default));
        Assert.Equal(0, device.Taps);
        if (!expected)
            Assert.False(scope.Matches(Binding, "123", Target));
    }

    [Fact]
    public async Task RepeatChecksRequireNewNativeEvidenceEveryTime()
    {
        var (ld, device) = Fake();
        var scope = Scope();
        var lookups = 0;
        for (var n = 0; n < 3; n++)
            Assert.True(
                await scope.VerifyAsync(
                    ld,
                    null,
                    _ =>
                    {
                        lookups++;
                        return Task.FromResult(true);
                    },
                    default
                )
            );
        Assert.Equal(3, lookups);
        Assert.Equal(6, device.AuthReads); // Before and after each native lookup.
    }

    [Theory]
    [InlineData("token")]
    [InlineData("owner")]
    [InlineData("process")]
    [InlineData("foreground")]
    public async Task ChangedSessionStopsBeforeNativeNavigation(string kind)
    {
        var (ld, device) = Fake();
        switch (kind)
        {
            case "token":
                device.AuthToken += "changed";
                break;
            case "owner":
                device.OwnerUid = "100000000000099";
                break;
            case "process":
                device.Pid = "124";
                break;
            case "foreground":
                device.Activity = "other.app/Activity";
                break;
        }
        var navigated = false;
        Assert.False(
            await Scope()
                .VerifyAsync(
                    ld,
                    null,
                    _ =>
                    {
                        navigated = true;
                        return Task.FromResult(true);
                    },
                    default
                )
        );
        Assert.False(navigated);
    }

    [Fact]
    public async Task SessionChangeDuringLookupAndUnsupportedUiDoNotAuthorize()
    {
        var (ld, device) = Fake();
        Assert.False(
            await Scope()
                .VerifyAsync(
                    ld,
                    null,
                    _ =>
                    {
                        device.AuthToken += "changed";
                        return Task.FromResult(true);
                    },
                    default
                )
        );
        device.AuthToken = Token;
        Assert.False(await Scope().VerifyAsync(ld, null, _ => Task.FromResult(false), default));
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var (ld, _) = Fake();
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Scope()
                .VerifyAsync(
                    ld,
                    null,
                    _ =>
                    {
                        cts.Cancel();
                        return Task.FromResult(true);
                    },
                    cts.Token
                )
        );
    }

    [Fact]
    public void ProofCannotBeLoadedFromHistoryOrMovedToAnotherTarget()
    {
        var scope = Scope();
        var target = Target;
        Assert.True(scope.Matches(Binding, "123", target));
        target.PageId = Owner;
        Assert.False(scope.Matches(Binding, "123", target));
        Assert.False(scope.Matches(Binding with { InstanceIdentity = "different" }, "123", Target));
        Assert.False(scope.Matches(Binding, "124", Target));
        Assert.Null(
            PFlowNativeIdentityScope.FromVerifiedGraph(
                Binding,
                "123",
                Target,
                new(Page, "Z Outdoors+", Owner, null)
            )
        );
        Assert.Null(PFlowNativeIdentityScope.FromVerifiedGraph(Binding, "", Target, Graph));
        Assert.Null(
            PFlowNativeIdentityScope.FromVerifiedGraph(
                Binding,
                "123",
                Target,
                Graph with
                {
                    Id = Owner,
                }
            )
        );
        Assert.Null(
            PFlowNativeIdentityScope.FromVerifiedGraph(
                Binding,
                "123",
                Target,
                Graph with
                {
                    UnderlyingUid = Page,
                }
            )
        );
        Assert.Null(
            PFlowNativeIdentityScope.FromVerifiedGraph(
                Binding with
                {
                    AppPackage = "other.app",
                },
                "123",
                Target,
                Graph
            )
        );
        var job = new PFlowJob { NativeIdentityScope = scope };
        var json = JsonSerializer.Serialize(job);
        Assert.DoesNotContain("NativeIdentityScope", json);
        Assert.DoesNotContain(Token, json);
        Assert.Null(JsonSerializer.Deserialize<PFlowJob>(json)!.NativeIdentityScope);
        var graphJson = JsonSerializer.Serialize(Graph);
        Assert.DoesNotContain("AuthenticationFingerprint", graphJson);
        Assert.DoesNotContain(Token, Graph.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealPageRouteChecksOwnProfileUiWithoutSettingsOrGraph(bool match)
    {
        var (ld, device) = Fake();
        device.WrongName = !match;
        Assert.Equal(match, await Scope().VerifyAsync(ld, null, default));
        Assert.Equal(0, device.Switches);
        Assert.Equal(0, device.Restarts);
        Assert.Equal(0, device.Taps);
        Assert.Equal(0, device.LinkReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealPersonalRouteChecksOwnProfileUiWithoutSettings(bool match)
    {
        var (ld, device) = Fake();
        device.Personal = true;
        device.WrongName = !match;
        var target = new FacebookPageTarget
        {
            PageId = Owner,
            UserUid = Owner,
            PageName = "Leon Gallagher",
            Type = "Profile",
        };
        var graph = new PFlowGraphIdentity(Owner, target.PageName, Owner, null)
        {
            AuthenticationFingerprint = Graph.AuthenticationFingerprint,
        };
        var scope = PFlowNativeIdentityScope.FromVerifiedGraph(Binding, "123", target, graph)!;
        Assert.Equal(match, await scope.VerifyAsync(ld, null, default));
        Assert.Equal(0, device.Switches);
        Assert.Equal(0, device.Restarts);
        Assert.Equal(0, device.Taps);
        Assert.False(device.InSettings);
    }

    [Theory]
    [InlineData("hidden-name")]
    [InlineData("disabled-name")]
    [InlineData("visitor")]
    [InlineData("input")]
    [InlineData("foreign")]
    [InlineData("settings")]
    [InlineData("switcher")]
    [InlineData("contradictory-actor")]
    public async Task UnsafeOrContradictoryUiStopsWithoutNavigation(string kind)
    {
        var (ld, device) = Fake();
        var xml = device.ProfileXml();
        if (kind is "hidden-name" or "disabled-name")
        {
            var root = XElement.Parse(xml);
            foreach (
                var node in root.Descendants("node")
                    .Where(n =>
                        ((string?)n.Attribute("text") ?? "").Trim() == Target.PageName
                        || ((string?)n.Attribute("content-desc") ?? "").Trim() == Target.PageName
                    )
            )
                node.SetAttributeValue(
                    kind == "hidden-name" ? "visible-to-user" : "enabled",
                    "false"
                );
            xml = root.ToString();
        }
        else if (kind == "visitor")
            xml = xml.Replace("Business Dashboard", "Follow");
        else if (kind == "input")
            xml = xml.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' class='android.widget.EditText' text='draft'/></hierarchy>"
            );
        else if (kind == "foreign")
            xml = xml.Replace("com.facebook.katana", "other.app");
        else if (kind == "settings")
            xml =
                "<hierarchy><node package='com.facebook.katana' text='Page Settings'/></hierarchy>";
        else if (kind == "switcher")
            xml = File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowScreenTime",
                    "ld2-switcher-full.xml"
                )
            );
        else
            xml = xml.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' text='Posting as Another Page'/></hierarchy>"
            );
        device.UiOverride = xml;
        Assert.False(await Scope().VerifyAsync(ld, null, default));
        Assert.Equal(0, device.Taps);
        Assert.Equal(0, device.Switches);
        Assert.Equal(0, device.Restarts);
    }

    [Fact]
    public async Task FailedProofCannotBeRevivedWithoutFreshGraph()
    {
        var (ld, device) = Fake();
        var scope = Scope();
        device.Pid = "124";
        Assert.False(await scope.VerifyAsync(ld, null, default));
        device.Pid = "123";
        Assert.False(await scope.VerifyAsync(ld, null, default));
        Assert.False(scope.Matches(Binding, "123", Target));
        Assert.True(await Scope().VerifyAsync(ld, null, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionUiMethodRequiresMatchingGraphBoundJobScope(bool attachProof)
    {
        var (ld, device) = Fake();
        var scope = Scope();
        Assert.True(await scope.VerifyAsync(ld, null, default));
        var job = new PFlowJob
        {
            DeviceIndex = Binding.DeviceIndex,
            AppPackage = Binding.AppPackage,
            AccountBinding = Binding,
            TargetId = Page,
            TargetName = Target.PageName,
            OwnerUid = Owner,
            NativeIdentityScope = attachProof ? scope : null,
        };
        var session = new PFlowPostTargetSession();
        Task<bool> Verify() =>
            session.VerifyAsync(
                job,
                _ => Task.FromResult("Session UI"),
                _ => Task.FromResult(true),
                _ => Task.FromResult(device.ProfileXml()),
                default
            );
        if (attachProof)
            Assert.True(await Verify());
        else
            await Assert.ThrowsAsync<InvalidOperationException>(Verify);
    }

    [Fact]
    public void DuplicateMappedNamesAndChangedGraphNameAreRejected()
    {
        var duplicate = new FacebookPageTarget
        {
            UserUid = Owner,
            PageId = "100000000000003",
            PageName = "  Z  Outdoors+ ",
            Type = "Page",
        };
        Assert.False(PFlowSessionProfileUi.UnambiguousMapping(Target, new[] { Target, duplicate }));
        duplicate.UserUid = "100000000000099";
        Assert.True(PFlowSessionProfileUi.UnambiguousMapping(Target, new[] { Target, duplicate }));
        Assert.True(PFlowSessionProfileUi.UnambiguousMapping(Target, new[] { Target, Target }));
        Assert.Null(
            PFlowNativeIdentityScope.FromVerifiedGraph(
                Binding,
                "123",
                Target,
                Graph with
                {
                    Name = "Renamed Page",
                }
            )
        );
    }

    [Fact]
    public async Task OwnProfileNavigationUsesOnlyCurrentActorControl()
    {
        var (ld, device) = Fake();
        device.UiOverride =
            "<hierarchy><node package='com.facebook.katana' text='Go to profile' class='android.widget.Button' enabled='true' clickable='true' bounds='[10,10][110,80]'/></hierarchy>";
        device.OnTap = () => device.UiOverride = null;
        Assert.True(await Scope().VerifyAsync(ld, null, default));
        Assert.Equal(1, device.Taps);
        Assert.False(device.InSettings);
        Assert.Equal(0, device.Switches);
        Assert.Equal(0, device.Restarts);
    }

    [Fact]
    public async Task UiOrSessionChangingDuringObservationInvalidatesProof()
    {
        var (ld, device) = Fake();
        var valid = device.ProfileXml();
        device.UiSequence.Enqueue(valid);
        device.UiSequence.Enqueue(valid);
        device.UiSequence.Enqueue(valid.Replace("Z Outdoors+", "Another Page"));
        var scope = Scope();
        Assert.False(await scope.VerifyAsync(ld, null, default));
        Assert.False(scope.Matches(Binding, "123", Target));
        device.OnUiRead = () => device.AuthToken += "changed";
        Assert.False(await Scope().VerifyAsync(ld, null, default));
        Assert.Equal(0, device.Taps);
    }

    public class Device : DispatchProxy
    {
        public string AuthToken = Token,
            OwnerUid = Owner,
            Pid = "123",
            Activity = "com.facebook.katana/FacebookActivity",
            VisibleUid = Page;
        public int AuthReads,
            Taps,
            Switches,
            Restarts,
            LinkReads;
        public bool ChangeVisibleLink,
            InSettings,
            Personal,
            WrongName;
        public string? UiOverride;
        public Queue<string> UiSequence = new();
        public Action? OnTap,
            OnUiRead;

        public string ProfileXml() =>
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    Personal ? "PFlowLogin" : "PFlowLayouts",
                    Personal
                        ? "ld74-facebook-581-own-profile.xml"
                        : "ld56-page-business-dashboard.xml"
                )
            );

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(ILDPlayerService.GetCurrentFocusedActivityAsync):
                    return Task.FromResult<string?>(Activity);
                case nameof(ILDPlayerService.ExecuteAdbAsync):
                    var command = (string)args![1]!;
                    if (command.Contains("pidof"))
                        return Task.FromResult(Pid);
                    if (command.EndsWith("authentication"))
                    {
                        AuthReads++;
                        return Task.FromResult(
                            "access_token\n" + AuthToken + "\nuser_id\n" + OwnerUid
                        );
                    }
                    if (command.EndsWith("underlying_account"))
                        return Task.FromResult("underlying_account_uid\n" + OwnerUid);
                    throw new NotSupportedException(command);
                case nameof(ILDPlayerService.DumpUiHierarchyAsync):
                    OnUiRead?.Invoke();
                    if (UiSequence.Count > 0)
                        return Task.FromResult<string?>(UiSequence.Dequeue());
                    if (InSettings)
                    {
                        var uid = ChangeVisibleLink && ++LinkReads > 1 ? Owner : VisibleUid;
                        return Task.FromResult<string?>(
                            "<hierarchy><node package='com.facebook.katana' text='Profile settings'/><node package='com.facebook.katana' text='Your "
                                + (Personal ? "profile" : "Page")
                                + " link'/><node package='com.facebook.katana' text='https://www.facebook.com/profile.php?id="
                                + uid
                                + "'/></hierarchy>"
                        );
                    }
                    var xml = UiOverride ?? ProfileXml();
                    if (WrongName)
                        xml = xml.Replace(
                            Personal ? "Leon Gallagher" : "Z Outdoors+",
                            "Another Page"
                        );
                    return Task.FromResult<string?>(xml);
                case nameof(ILDPlayerService.TapAsync):
                    Taps++;
                    if (OnTap != null)
                    {
                        OnTap();
                        return Task.FromResult(true);
                    }
                    InSettings = true;
                    return Task.FromResult(true);
                case nameof(ILDPlayerService.PressBackAsync):
                    InSettings = false;
                    return Task.FromResult(true);
                case nameof(ILDPlayerService.SwitchToPersonaAsync):
                    Switches++;
                    throw new InvalidOperationException(
                        "Repeat verification must not switch persona"
                    );
                case nameof(ILDPlayerService.StopFacebookAppAsync):
                    Restarts++;
                    throw new InvalidOperationException("No restart allowed");
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
