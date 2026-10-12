using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowTaskRoutingTests
{
    private static FacebookPageTarget Target(string owner, string id = "77") =>
        new()
        {
            UserUid = owner,
            PageId = id,
            PageName = "Shared name",
            AppClone = "FB-01",
            AccountBinding = new("fixture", 900181, "fixture-LD", "com.facebook.katana", owner),
        };

    [Theory]
    [InlineData("77")]
    [InlineData("")]
    public void LegacyRowsCannotChooseAnOwnerForSharedPage(string id)
    {
        var a = Target("123");
        var b = Target("456");
        var task = new AutoPostTaskItem { PageId = id, Page = "Shared name" };
        Assert.False(PFlowTaskRouting.MatchesTarget(task, a, [a, b]));
        Assert.False(PFlowTaskRouting.MatchesTarget(task, b, [a, b]));
    }

    [Fact]
    public void LegacySameNameAcrossDifferentPagesIsAmbiguous()
    {
        var a = Target("123");
        var b = Target("456", "88");
        var task = new AutoPostTaskItem { Page = "Shared name" };
        Assert.False(PFlowTaskRouting.MatchesTarget(task, a, [a, b]));
        Assert.False(PFlowTaskRouting.MatchesTarget(task, b, [a, b]));
    }

    [Fact]
    public void BoundQueueRowsChooseOnlyTheirProfileAndApp()
    {
        var a = Target("123");
        var b = Target("456");
        var task = new AutoPostTaskItem
        {
            PageId = "77",
            OwnerUid = "456",
            AccountBinding = b.AccountBinding,
        };
        Assert.False(PFlowTaskRouting.MatchesTarget(task, a, [a, b]));
        Assert.True(PFlowTaskRouting.MatchesTarget(task, b, [a, b]));
        task.App = "FB-02";
        Assert.False(PFlowTaskRouting.MatchesTarget(task, b, [a, b]));
    }

    [Fact]
    public void DifferentDeviceBindingCannotBorrowSameOwner()
    {
        var a = Target("123");
        var task = new AutoPostTaskItem
        {
            PageId = "77",
            OwnerUid = "123",
            AccountBinding = a.AccountBinding! with { DeviceIndex = 900182 },
        };
        Assert.False(PFlowTaskRouting.MatchesTarget(task, a, [a]));
    }

    [Fact]
    public void SingleTargetLegacyQueuesRemainCompatible()
    {
        var a = Target("123");
        Assert.True(PFlowTaskRouting.MatchesTarget(new() { Page = "Shared name" }, a, [a]));
        Assert.True(PFlowTaskRouting.MatchesTarget(new() { PageId = "77" }, a, [a]));
    }

    [Fact]
    public void ProfileAndPageRowsDoNotShareTypedQueueJobs()
    {
        var a = Target("123");
        a.Type = "Profile";
        Assert.False(
            PFlowTaskRouting.MatchesTarget(new() { PageId = "77", OwnerUid = "123" }, a, [a])
        );
        Assert.True(
            PFlowTaskRouting.MatchesTarget(
                new()
                {
                    PageId = "77",
                    OwnerUid = "123",
                    IsProfile = true,
                },
                a,
                [a]
            )
        );
    }
}
