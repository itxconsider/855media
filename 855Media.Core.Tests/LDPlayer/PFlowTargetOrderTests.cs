using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowTargetOrderTests
{
    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        1,
        "LD-1",
        "com.facebook.katana",
        "100000000000001"
    );

    private static FacebookPageTarget Page(string id, string name) =>
        new()
        {
            PageId = id,
            PageName = name,
            UserUid = Binding.OwnerUid,
            AccountBinding = Binding,
            Type = "Page",
        };

    private static PFlowGraphIdentity Graph(FacebookPageTarget page) =>
        new(page.PageId, page.PageName, Binding.OwnerUid, null)
        {
            AuthenticationFingerprint = "verified-session",
        };

    private static FacebookPageTarget[] Order(
        FacebookPageTarget[] pages,
        PFlowGraphIdentity? graph
    ) => PFlowTargetOrder.PreferActive(pages, p => p, Binding, graph);

    [Fact]
    public void ActivePageMovesBeforePersonalAndOtherPagesWithoutChangingTheirOrder()
    {
        var personal = Page(Binding.OwnerUid, "Owner");
        personal.Type = "Profile";
        var b = Page("200000000000002", "B");
        var a = Page("200000000000003", "A");
        var d = Page("200000000000004", "D");
        var original = new[] { personal, b, a, d };
        Assert.Equal(new[] { a, personal, b, d }, Order(original, Graph(a)));
        Assert.Equal(new[] { personal, b, a, d }, original);
    }

    [Fact]
    public void ActiveWithoutQueuedJobsDoesNotGetInserted()
    {
        var b = Page("200000000000002", "B");
        Assert.Equal(new[] { b }, Order([b], Graph(Page("200000000000003", "A"))));
        Assert.Empty(Order([], Graph(b)));
    }

    [Fact]
    public void PersonaUidCanMatchVerifiedActor()
    {
        var b = Page("200000000000002", "B");
        var a = Page("200000000000003", "A");
        a.PersonaUid = "300000000000003";
        Assert.Equal(new[] { a, b }, Order([b, a], Graph(a) with { Id = a.PersonaUid }));
    }

    [Fact]
    public void ActivePersonalCanAlsoBePrioritized()
    {
        var b = Page("200000000000002", "B");
        var owner = Page(Binding.OwnerUid, "Owner");
        owner.Type = "Profile";
        Assert.Equal(new[] { owner, b }, Order([b, owner], Graph(owner)));
    }

    [Fact]
    public void MissingOrStaleIdentityKeepsFallbackOrder()
    {
        var b = Page("200000000000002", "B");
        var a = Page("200000000000003", "A");
        foreach (
            var graph in new PFlowGraphIdentity?[]
            {
                null,
                Graph(a) with
                {
                    UnderlyingUid = "other-owner",
                },
                Graph(a) with
                {
                    AuthenticationFingerprint = "",
                },
                Graph(a) with
                {
                    Name = "Changed name",
                },
            }
        )
            Assert.Equal(new[] { b, a }, Order([b, a], graph));
    }

    [Fact]
    public void DifferentDeviceOrOwnerMappingCannotBePromoted()
    {
        var b = Page("200000000000002", "B");
        var a = Page("200000000000003", "A");
        a.AccountBinding = Binding with { DeviceIndex = 2 };
        Assert.Equal(new[] { b, a }, Order([b, a], Graph(a)));
        a.AccountBinding = Binding;
        a.UserUid = "other-owner";
        Assert.Equal(new[] { b, a }, Order([b, a], Graph(a)));
    }

    [Fact]
    public void AmbiguousActorMappingsPreserveOrder()
    {
        var b = Page("200000000000002", "B");
        var a = Page("200000000000003", "A");
        var duplicate = Page(a.PageId, a.PageName);
        Assert.Equal(new[] { b, a, duplicate }, Order([b, a, duplicate], Graph(a)));
    }

    [Fact]
    public void PrioritizationMovesTargetReferencesWithoutChangingJobs()
    {
        var b = Page("200000000000002", "B");
        var a = Page("200000000000003", "A");
        var first = (Target: b, Parts: new[] { 1, 2, 3 });
        var second = (Target: a, Parts: new[] { 1, 2, 3 });
        var ordered = PFlowTargetOrder.PreferActive(
            new[] { first, second },
            row => row.Target,
            Binding,
            Graph(a)
        );
        Assert.Same(a, ordered[0].Target);
        Assert.Same(second.Parts, ordered[0].Parts);
        Assert.Equal(new[] { 1, 2, 3 }, ordered[0].Parts);
        Assert.Same(first.Parts, ordered[1].Parts);
    }
}
