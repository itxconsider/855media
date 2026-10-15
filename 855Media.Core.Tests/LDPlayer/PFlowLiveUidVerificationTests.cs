using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowLiveUidVerificationTests
{
    [Theory]
    [InlineData("profile")]
    [InlineData("page")]
    [InlineData("persona")]
    public async Task GraphFirstMatchNeverSelectsTargetOrLooksUpUi(string kind)
    {
        var reads = 0;
        var links = 0;
        var target = Target(kind == "profile");
        var method = await PFlowLiveUidVerification.ResolveAsync(
            target,
            _ =>
            {
                reads++;
                return Task.FromResult<PFlowGraphIdentity?>(
                    new(
                        kind == "profile" ? Owner
                            : kind == "persona" ? Persona
                            : Page,
                        "Fixture",
                        Owner,
                        null
                    )
                );
            },
            NoUi,
            NoUi,
            (_, _, _) =>
            {
                links++;
                return Task.FromResult(true);
            },
            default
        );
        Assert.Equal("Graph UID", method);
        Assert.Equal(1, reads);
        Assert.Equal(kind == "persona" ? 1 : 0, links);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DifferentActorUnderSameOwnerSwitchesOnceThenReadsGraphAgain(bool profile)
    {
        var events = new List<string>();
        var target = Target(profile);
        var method = await PFlowLiveUidVerification.ResolveAsync(
            target,
            _ =>
            {
                events.Add("graph");
                return Task.FromResult<PFlowGraphIdentity?>(
                    new(
                        events.Count == 1 ? (profile ? Page : Owner) : target.PageId,
                        "Fixture",
                        Owner,
                        null
                    )
                );
            },
            _ =>
            {
                events.Add("switch");
                return Task.FromResult(true);
            },
            NoUi,
            (_, _, _) => Task.FromResult(true),
            default
        );
        Assert.Equal("Graph UID", method);
        Assert.Equal(new[] { "graph", "switch", "graph" }, events);
    }

    [Fact]
    public async Task AcknowledgedSwitchWithoutChangedGraphCannotAuthorizeWork()
    {
        var switches = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowLiveUidVerification.ResolveAsync(
                Target(),
                _ => Task.FromResult<PFlowGraphIdentity?>(new(Owner, "Fixture", Owner, null)),
                _ =>
                {
                    switches++;
                    return Task.FromResult(true);
                },
                NoUi,
                (_, _, _) => Task.FromResult(true),
                default
            )
        );
        Assert.Equal(1, switches);
    }

    [Fact]
    public async Task GraphUnavailableUsesUiWithoutPreemptiveSelection()
    {
        var lookups = 0;
        Assert.Equal(
            "UI UID lookup",
            await PFlowLiveUidVerification.ResolveAsync(
                Target(),
                _ => Task.FromResult<PFlowGraphIdentity?>(null),
                NoUi,
                _ =>
                {
                    lookups++;
                    return Task.FromResult(true);
                },
                (_, _, _) => Task.FromResult(false),
                default
            )
        );
        Assert.Equal(1, lookups);
    }

    [Theory]
    [InlineData("999", "999")]
    [InlineData("", "999")]
    [InlineData(Persona, Owner)]
    public async Task WrongOwnerOrUnverifiedDelegationNeverTriggersUiFallback(
        string actor,
        string owner
    )
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowLiveUidVerification.ResolveAsync(
                Target(),
                _ => Task.FromResult<PFlowGraphIdentity?>(new(actor, "Fixture", owner, null)),
                NoUi,
                NoUi,
                (_, _, _) => Task.FromResult(false),
                default
            )
        );
    }

    [Fact]
    public async Task CancelledGraphRouteDoesNotReadOrNavigate()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowLiveUidVerification.ResolveAsync(
                Target(),
                _ => throw new Xunit.Sdk.XunitException("Unexpected Graph read"),
                NoUi,
                NoUi,
                (_, _, _) => Task.FromResult(false),
                new CancellationToken(true)
            )
        );
    }

    [Theory]
    [InlineData("999", "")]
    [InlineData("", "999")]
    public async Task IncompleteGraphResponseDoesNotEraseAKnownMismatch(string actor, string owner)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowLiveUidVerification.RequireAsync(
                Target(true),
                _ => Task.FromResult<PFlowGraphIdentity?>(new(actor, "Fixture", owner, null)),
                NoUi,
                (_, _, _) => Task.FromResult(false),
                default
            )
        );
    }

    private const string Owner = "61594582829499";
    private const string Persona = "61594688834095";
    private const string Page = "1407114619142015";

    private static FacebookPageTarget Target(bool profile = false) =>
        new()
        {
            Type = profile ? "Profile" : "Page",
            UserUid = Owner,
            PageId = profile ? Owner : Page,
            PersonaUid = profile ? Owner : Persona,
            PageName = "Fixture target",
        };

    private static Task<bool> NoUi(CancellationToken _) =>
        throw new Xunit.Sdk.XunitException("Unexpected UI fallback");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FreshGraphChecksRunForEveryOperation(bool profile)
    {
        var reads = 0;
        var target = Target(profile);
        Task<PFlowGraphIdentity?> Read(CancellationToken _)
        {
            reads++;
            return Task.FromResult<PFlowGraphIdentity?>(
                new(profile ? Owner : Page, "Same name is not proof", Owner, null)
            );
        }
        for (var operation = 0; operation < 2; operation++)
            Assert.Equal(
                "Graph UID",
                await PFlowLiveUidVerification.RequireAsync(
                    target,
                    Read,
                    NoUi,
                    (_, _, _) => Task.FromResult(false),
                    default
                )
            );
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task DistinctPagePersonaRequiresFreshDelegation()
    {
        var links = 0;
        var method = await PFlowLiveUidVerification.RequireAsync(
            Target(),
            _ => Task.FromResult<PFlowGraphIdentity?>(new(Persona, "Fixture", Owner, null)),
            NoUi,
            (actor, pageId, _) =>
            {
                links++;
                Assert.Equal(Persona, actor);
                Assert.Equal(Page, pageId);
                return Task.FromResult(true);
            },
            default
        );
        Assert.Equal("Graph UID", method);
        Assert.Equal(1, links);
    }

    [Theory]
    [InlineData("wrong-owner")]
    [InlineData("wrong-actor")]
    [InlineData("personal-token-for-page")]
    [InlineData("unlinked-persona")]
    public async Task PositiveGraphMismatchStopsWithoutFallingBackToNamesOrUi(string problem)
    {
        var graph = new PFlowGraphIdentity(
            problem == "wrong-owner" ? Page
                : problem == "personal-token-for-page" ? Owner
                : problem == "unlinked-persona" ? Persona
                : "999",
            "Fixture target",
            problem == "wrong-owner" ? "999" : Owner,
            null
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowLiveUidVerification.RequireAsync(
                Target(),
                _ => Task.FromResult<PFlowGraphIdentity?>(graph),
                NoUi,
                (_, _, _) => Task.FromResult(false),
                default
            )
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnavailableGraphRequiresNewUiLookupEveryTime(bool missingOwner)
    {
        var calls = 0;
        for (var operation = 0; operation < 2; operation++)
        {
            var method = await PFlowLiveUidVerification.RequireAsync(
                Target(true),
                _ =>
                    Task.FromResult<PFlowGraphIdentity?>(
                        missingOwner ? new(Owner, "Fixture", "", null) : null
                    ),
                _ =>
                {
                    calls++;
                    return Task.FromResult(true);
                },
                (_, _, _) => Task.FromResult(false),
                default
            );
            Assert.Equal("UI UID lookup", method);
        }
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedUiFallbackDoesNotAuthorizeWork()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowLiveUidVerification.RequireAsync(
                Target(true),
                _ => Task.FromResult<PFlowGraphIdentity?>(null),
                _ => Task.FromResult(false),
                (_, _, _) => Task.FromResult(false),
                default
            )
        );
    }

    [Fact]
    public async Task CancellationDoesNotFallBackOrAuthorize()
    {
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowLiveUidVerification.RequireAsync(
                Target(true),
                _ =>
                {
                    cts.Cancel();
                    return Task.FromResult<PFlowGraphIdentity?>(new(Owner, "Fixture", Owner, null));
                },
                NoUi,
                (_, _, _) => Task.FromResult(false),
                cts.Token
            )
        );
    }

    private static string Settings(string id = Page) =>
        "<hierarchy><node package='com.facebook.katana' text='Profile settings'/><node package='com.facebook.katana' text='Your Page link'/><node package='com.facebook.katana' text='https://www.facebook.com/profile.php?id="
        + id
        + "'/></hierarchy>";

    [Fact]
    public void PageUidLookupOnlyAcceptsItsOwnSettingsLink()
    {
        Assert.NotNull(PFlowLiveUidVerification.ReadOwnPageLink(Settings()));
        Assert.NotNull(
            PFlowLiveUidVerification.ReadOwnPageLink(
                Settings().Replace("Profile settings", "Page Settings")
            )
        );
        Assert.Null(
            PFlowLiveUidVerification.ReadOwnPageLink(
                Settings().Replace("Your Page link", "Shared link")
            )
        );
        Assert.Null(
            PFlowLiveUidVerification.ReadOwnPageLink(
                Settings().Replace("Your Page link", "Your profile link")
            )
        );
        Assert.Null(
            PFlowLiveUidVerification.ReadOwnPageLink(Settings().Replace("Profile settings", "Feed"))
        );
        Assert.Null(
            PFlowLiveUidVerification.ReadOwnPageLink(
                Settings().Replace("com.facebook.katana", "other.app")
            )
        );
        Assert.Null(
            PFlowLiveUidVerification.ReadOwnPageLink(
                Settings()
                    .Replace("</hierarchy>", "<node class='android.widget.EditText'/></hierarchy>")
            )
        );
    }

    public class Device : DispatchProxy
    {
        public string PageUid = Page;
        public bool LinkChanges;
        public int Reads,
            Taps,
            Switches;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(ILDPlayerService.DumpUiHierarchyAsync):
                    Reads++;
                    return Task.FromResult<string?>(
                        Reads == 1
                            ? "<hierarchy><node package='com.facebook.katana' text='Fixture target'/><node package='com.facebook.katana' content-desc='Profile settings' clickable='true' enabled='true' bounds='[620,80][710,170]'/></hierarchy>"
                            : Settings(LinkChanges && Reads > 2 ? "999" : PageUid)
                    );
                case nameof(ILDPlayerService.TapAsync):
                    Taps++;
                    return Task.FromResult(true);
                case nameof(ILDPlayerService.PressBackAsync):
                    return Task.FromResult(true);
                case nameof(ILDPlayerService.SwitchToPersonaAsync):
                    Switches++;
                    return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task NativePageLookupRequiresMatchingStableLiveUid(bool match, bool changes)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.PageUid = match ? Page : "999";
        device.LinkChanges = changes;
        var verified = await PFlowLiveUidVerification.LookupPageUidAsync(
            ld,
            19,
            "fixture",
            Target(),
            default
        );
        Assert.Equal(match && !changes, verified);
        Assert.Equal(1, device.Taps);
        Assert.Equal(verified ? 1 : 0, device.Switches);
    }
}
