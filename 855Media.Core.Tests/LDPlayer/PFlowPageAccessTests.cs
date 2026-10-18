using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPageAccessTests
{
    [Fact]
    public void SinglePageRefreshDoesNotInvalidateVerifiedSiblingAndFullScopeIncludesBoth()
    {
        var selected = new FacebookPageTarget
        {
            Type = "Page",
            PageId = "1406696292517646",
            PersonaUid = "61594923060519",
            LivePageAccessVerified = true,
        };
        var sibling = new FacebookPageTarget
        {
            Type = "Page",
            PageId = "1277227388811130",
            PersonaUid = "61594176890556",
            LivePageAccessVerified = true,
        };
        var scope = new[]
        {
            new FacebookPageInfo { PageId = selected.PageId, PersonaUid = selected.PersonaUid },
        };
        Assert.True(PFlowPageAccessReconciliation.IncludesPage(selected, scope));
        Assert.False(PFlowPageAccessReconciliation.IncludesPage(sibling, scope));
        Assert.True(sibling.LivePageAccessVerified);
        var all = new[]
        {
            scope[0],
            new FacebookPageInfo { PageId = sibling.PageId, PersonaUid = sibling.PersonaUid },
        };
        Assert.True(PFlowPageAccessReconciliation.IncludesPage(sibling, all));
        Assert.False(
            PFlowPageAccessReconciliation.IncludesPage(
                new FacebookPageTarget { Type = "Profile", PageId = selected.PageId },
                all
            )
        );
    }

    [Fact]
    public void RootCannotRestoreAccessOrEraseUnavailableStatus()
    {
        foreach (var previous in new[] { "", PFlowPageAccessReconciliation.Unavailable })
        {
            var page = new FacebookPageInfo
            {
                PageId = "123",
                LivePageAccessVerified = true,
                PageAccessIssue = previous,
            };
            PFlowPageAccessReconciliation.InvalidateRootCandidate(page);
            Assert.False(page.LivePageAccessVerified);
            Assert.Equal(
                previous == PFlowPageAccessReconciliation.Unavailable
                    ? previous
                    : PFlowPageAccessReconciliation.Unverified,
                page.PageAccessIssue
            );
            Assert.Equal("123", page.PageId);
        }
    }

    [Fact]
    public void FailedNavigationDoesNotClaimDeletionAndOnlyLiveVerificationRestoresAccess()
    {
        Assert.Equal(
            PFlowPageAccessReconciliation.Unverified,
            PFlowPageAccessReconciliation.ResultIssue(false, "", "<hierarchy/>")
        );
        Assert.Equal(
            PFlowPageAccessReconciliation.Unavailable,
            PFlowPageAccessReconciliation.ResultIssue(
                false,
                PFlowPageAccessReconciliation.Unavailable,
                "<hierarchy/>"
            )
        );
        Assert.Equal(
            "",
            PFlowPageAccessReconciliation.ResultIssue(
                true,
                PFlowPageAccessReconciliation.Unavailable,
                null
            )
        );
    }

    [Theory]
    [InlineData("com.facebook.katana", "true", "Access unavailable")]
    [InlineData("other.app", "true", "Unverified")]
    [InlineData("com.facebook.katana", "false", "Unverified")]
    public void UnavailableRequiresVisibleFacebookEvidence(
        string package,
        string visible,
        string expected
    )
    {
        var xml =
            $"<hierarchy><node package='{package}' visible-to-user='{visible}' text='You no longer have access to this Page'/></hierarchy>";
        Assert.Equal(expected, PFlowPageAccessReconciliation.ResultIssue(false, "", xml));
    }
}
