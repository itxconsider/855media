using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class FacebookDiscoveryRetentionTests
{
    private static FacebookPageTarget SavedPage(
        string pageId = "200001",
        string personaUid = "300001",
        string userUid = "100001",
        string device = "LD-6",
        string clone = "FB-01"
    ) =>
        new()
        {
            PageId = pageId,
            PersonaUid = personaUid,
            UserUid = userUid,
            DeviceName = device,
            AppClone = clone,
            PageName = "Saved Page",
            Type = "Page",
            CreationDate = "November 2022",
            Followers = 27,
            FollowersKnown = true,
            TitleVideoReel = "Saved posting title",
        };

    [Fact]
    public void SparseSnapshot_RetainsSameOwnerPageIdentityForUiVerification()
    {
        var saved = SavedPage();
        var discovered = new FacebookAccountDetails { Uid = saved.UserUid };
        var retained = discovered.RetainSavedPageIdentities([saved], "LD-6", "FB-01");

        Assert.Equal(1, retained);
        var page = Assert.Single(discovered.Pages);
        Assert.Equal(saved.PageId, page.PageId);
        Assert.Equal(saved.PersonaUid, page.PersonaUid);
        Assert.Equal(saved.PageName, page.PageName);
        Assert.Equal(saved.UserUid, page.UserUid);
        // Saved statistics remain in the table model; they are not presented
        // as newly scraped data or proof of current managed access.
        Assert.Empty(page.CreationDate);
        Assert.False(page.FollowersKnown);
        Assert.Equal("November 2022", saved.CreationDate);
        Assert.Equal("Saved posting title", saved.TitleVideoReel);
    }

    [Fact]
    public void ExistingRootIdentity_IsNotDuplicatedBySavedDelegateOrPersona()
    {
        var discovered = new FacebookAccountDetails
        {
            Uid = "100001",
            Pages = [new FacebookPageInfo { PageId = "200001", PersonaUid = "300001" }],
        };
        Assert.Equal(
            0,
            discovered.RetainSavedPageIdentities(
                [SavedPage(), SavedPage(personaUid: "300002"), SavedPage(pageId: "200002")],
                "LD-6",
                "FB-01"
            )
        );
        Assert.Single(discovered.Pages);
    }

    [Theory]
    [InlineData("LD-6", "FB-01", "100001", "300001", "", false, true)]
    [InlineData("Renamed Instance", "FB-01", "100001", "", "200001", false, true)]
    [InlineData("LD-6", "FB-02", "100001", "300001", "200001", false, false)]
    [InlineData("LD-6", "FB-01", "100002", "300001", "200001", false, false)]
    [InlineData("LD-7", "FB-01", "100001", "300001", "200001", false, false)]
    [InlineData("LD-6", "FB-01", "100001", "300001", "200001", true, false)]
    [InlineData("LD-6", "FB-01", "100001", "300002", "200002", false, false)]
    [InlineData("LD-6", "FB-01", "", "300001", "200001", false, false)]
    public void UiUpdateIdentityMatcher_ScopesDeviceCloneOwnerAndPageIdentity(
        string device,
        string clone,
        string owner,
        string persona,
        string pageId,
        bool profile,
        bool expected
    )
    {
        var saved = SavedPage();
        var alias = device == "Renamed Instance" ? "LD-6" : null;
        Assert.Equal(
            expected,
            saved.MatchesIdentity(device, clone, owner, persona, pageId, profile, alias)
        );
    }

    [Theory]
    [InlineData("200001", "300001", "100002", "LD-6", "FB-01")]
    [InlineData("200001", "300001", "100001", "LD-7", "FB-01")]
    [InlineData("200001", "300001", "100001", "LD-6", "FB-02")]
    [InlineData("", "300001", "100001", "LD-6", "FB-01")]
    [InlineData("New Page", "300001", "100001", "LD-6", "FB-01")]
    [InlineData("200001", "not-a-uid", "100001", "LD-6", "FB-01")]
    [InlineData("200001", "100001", "100001", "LD-6", "FB-01")]
    public void Retention_DoesNotMixOwnersDevicesClonesOrUnstableIdentities(
        string pageId,
        string personaUid,
        string owner,
        string device,
        string clone
    )
    {
        var discovered = new FacebookAccountDetails { Uid = "100001" };
        Assert.Equal(
            0,
            discovered.RetainSavedPageIdentities(
                [SavedPage(pageId, personaUid, owner, device, clone)],
                "LD-6",
                "FB-01"
            )
        );
        Assert.Empty(discovered.Pages);
    }

    [Fact]
    public void Retention_AcceptsInstanceAliasAndSkipsPersonalProfile()
    {
        var profile = SavedPage();
        profile.Type = "Profile";
        var discovered = new FacebookAccountDetails { Uid = "100001" };
        Assert.Equal(
            1,
            discovered.RetainSavedPageIdentities(
                [profile, SavedPage()],
                "Renamed Instance",
                "FB-01",
                "LD-6"
            )
        );
        Assert.Single(discovered.Pages);
    }
}
