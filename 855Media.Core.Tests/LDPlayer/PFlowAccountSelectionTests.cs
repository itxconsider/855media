using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowAccountSelectionTests
{
    private static PFlowPersonalAccount Account(string uid = "123", int index = 1) =>
        new()
        {
            Name = "Fixture owner",
            Binding = new("fixture-instance", index, "LD-" + index, "com.facebook.katana", uid),
        };

    private static PFlowAccountSelectionDraft Selection(
        PFlowPersonalAccount account,
        bool fetch = true,
        bool screenTime = true,
        bool posting = false,
        bool login = false,
        bool rotation = false,
        int order = 0
    ) => new(account.Binding, fetch, screenTime, posting, login, rotation, order);

    [Fact]
    public void ExistingSessionCanSelectFetchAndScreenTimeWithoutCredentials()
    {
        var account = Account();
        PFlowAccountSelections.Apply(new[] { account }, new[] { Selection(account) });
        Assert.True(account.SelectedForDiscovery);
        Assert.True(account.SelectedForScreenTime);
        Assert.False(account.AllowAutomaticLogin);
        Assert.False(account.AllowAccountRotation);
        Assert.Equal("", account.CredentialReference);
        Assert.Single(
            PFlowAutomaticSessionService.Plan(
                new[] { account },
                1,
                "com.facebook.katana",
                true,
                false
            )
        );
    }

    [Fact]
    public void DraftsReplacePreviouslySavedUncheckedSelections()
    {
        var account = Account();
        PFlowAccountSelections.Apply(
            new[] { account },
            new[] { Selection(account, posting: true) }
        );
        Assert.True(account.SelectedForScreenTime);
        Assert.True(account.SelectedForPosting);
        PFlowAccountSelections.Apply(
            new[] { account },
            new[] { Selection(account, fetch: false, screenTime: false, posting: false) }
        );
        Assert.False(account.SelectedForDiscovery);
        Assert.False(account.SelectedForScreenTime);
        Assert.False(account.SelectedForPosting);
    }

    [Fact]
    public void RotationWithStoredCredentialsCanBeSelected()
    {
        var account = Account();
        account.CredentialReference = "fixture-protected-reference";
        PFlowAccountSelections.Apply(
            new[] { account },
            new[] { Selection(account, login: true, rotation: true, order: 3) }
        );
        Assert.True(account.AllowAutomaticLogin);
        Assert.True(account.AllowAccountRotation);
        Assert.Equal(3, account.RotationOrder);
    }

    [Theory]
    [InlineData("missing-credentials")]
    [InlineData("rotation-without-login")]
    [InlineData("negative-order")]
    [InlineData("changed-binding")]
    [InlineData("missing-account")]
    public void InvalidLaterAccountLeavesAllSelectionsUnchanged(string issue)
    {
        var first = Account();
        var second = Account("456", 2);
        var draft = Selection(second);
        draft = issue switch
        {
            "missing-credentials" => draft with { AutomaticLogin = true },
            "rotation-without-login" => draft with { AccountRotation = true },
            "negative-order" => draft with { Order = -1 },
            "changed-binding" => draft with
            {
                Binding = second.Binding with { InstanceIdentity = "replacement" },
            },
            _ => draft with { Binding = second.Binding with { OwnerUid = "999" } },
        };
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountSelections.Apply(new[] { first, second }, new[] { Selection(first), draft })
        );
        Assert.False(first.SelectedForDiscovery);
        Assert.False(first.SelectedForScreenTime);
        Assert.False(second.SelectedForScreenTime);
    }

    [Fact]
    public void SavingOneDevicesDraftDoesNotSelectOrChangeOtherDevices()
    {
        var first = Account();
        var second = Account("456", 2);
        second.AllowAutomaticLogin = true;
        PFlowAccountSelections.Apply(new[] { first, second }, new[] { Selection(first) });
        Assert.True(first.SelectedForScreenTime);
        Assert.False(second.SelectedForScreenTime);
        Assert.True(second.AllowAutomaticLogin);
    }

    [Fact]
    public void DuplicateDraftsAreRejectedBeforeMutation()
    {
        var account = Account();
        Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountSelections.Apply(
                new[] { account },
                new[] { Selection(account), Selection(account, screenTime: false) }
            )
        );
        Assert.False(account.SelectedForScreenTime);
    }

    [Fact]
    public void ClearedCredentialDetectedBetweenUiSnapshotAndSaveIsRejected()
    {
        var account = Account();
        account.CredentialReference = "fixture-reference";
        var draft = Selection(account, login: true, rotation: true);
        account.CredentialReference = "";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PFlowAccountSelections.Apply(new[] { account }, new[] { draft })
        );
        Assert.Contains("LD-1 / Fixture owner", ex.Message);
        Assert.False(account.SelectedForScreenTime);
    }
}
