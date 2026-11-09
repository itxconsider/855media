using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSinglePageFetchRecoveryTests
{
    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        33,
        "LD-33",
        "com.facebook.katana",
        "61586306443979"
    );

    private static FacebookPageTarget Pending() =>
        new()
        {
            PageName = "Danny Stenhouse",
            DeviceName = "LD-33",
            AppClone = "FB-01",
            Type = "Page",
            ManagedBy = "Rudolph White",
            FetchDetailsPending = true,
        };

    private static PFlowPersonalAccount Owner() =>
        new() { Binding = Binding, Name = "Rudolph White" };

    [Fact]
    public void PendingNameSelectsOnlyItsNamedSavedOwnerCandidate()
    {
        var owner = Owner();
        Assert.Same(owner, PFlowSinglePageFetchRecovery.OwnerCandidate(Pending(), [owner], 33));
        Assert.Empty(Pending().UserUid);
    }

    [Theory]
    [InlineData("no-hint")]
    [InlineData("wrong-hint")]
    [InlineData("duplicate")]
    [InlineData("wrong-device")]
    [InlineData("foreign-binding")]
    [InlineData("not-pending")]
    public void UnboundPendingOwnerCannotBeGuessedFromDeviceAlone(string mode)
    {
        var target = Pending();
        var owner = Owner();
        if (mode == "no-hint")
            target.ManagedBy = "";
        if (mode == "wrong-hint")
            target.ManagedBy = "Other owner";
        if (mode == "wrong-device")
            owner.Binding = Binding with { DeviceIndex = 11 };
        if (mode == "foreign-binding")
            target.AccountBinding = Binding with { InstanceIdentity = "other" };
        if (mode == "not-pending")
            target.FetchDetailsPending = false;
        Assert.Throws<InvalidOperationException>(() =>
            PFlowSinglePageFetchRecovery.OwnerCandidate(
                target,
                mode == "duplicate" ? [owner, Owner()] : [owner],
                33
            )
        );
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("unverified", false)]
    [InlineData("owner", false)]
    [InlineData("name", false)]
    [InlineData("uid", false)]
    [InlineData("existing-id", false)]
    [InlineData("changed-row", false)]
    public void PendingIdentityRequiresFreshExactOwnerPageAndNumericIds(string mode, bool expected)
    {
        var target = Pending();
        var fresh = new FacebookPageInfo
        {
            PageName = target.PageName,
            UserUid = Binding.OwnerUid,
            PageId = "61593547578002",
            PersonaUid = "61593547578002",
            LivePageAccessVerified = true,
        };
        if (mode == "unverified")
            fresh.LivePageAccessVerified = false;
        if (mode == "owner")
            fresh.UserUid = "999999999999";
        if (mode == "name")
            fresh.PageName = "Other Page";
        if (mode == "uid")
            fresh.PersonaUid = "";
        if (mode == "existing-id")
            target.PageId = "999999999999";
        if (mode == "changed-row")
            target.FetchDetailsPending = false;
        Assert.Equal(
            expected,
            PFlowSinglePageFetchRecovery.FreshPageMatches(target, fresh, Binding)
        );
    }

    [Fact]
    public void ChangedPendingOwnerOrIdentityCannotBeCommitted()
    {
        var target = Pending();
        var current = target.CreateSnapshot();
        Assert.True(PFlowSinglePageFetchRecovery.Unchanged(target, current));
        current.ManagedBy = "Other owner";
        Assert.False(PFlowSinglePageFetchRecovery.Unchanged(target, current));
        current = target.CreateSnapshot();
        current.UserUid = Binding.OwnerUid;
        Assert.False(PFlowSinglePageFetchRecovery.Unchanged(target, current));
    }
}
