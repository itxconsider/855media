using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    private const string PageIdentityXml = """
        <hierarchy><node package="com.facebook.katana" bounds="[0,0][330,580]">
        <node package="com.facebook.katana" text="Confirm your identity" bounds="[10,140][320,165]"/>
        <node package="com.facebook.katana" text="Before you can publish as the Page, we need to confirm it's really you." bounds="[10,170][320,215]"/>
        <node package="com.facebook.katana" text="We detected unusual activity" bounds="[40,260][320,285]"/>
        <node package="com.facebook.katana" text="Verify via SMS" bounds="[40,330][320,355]"/>
        <node package="com.facebook.katana" text="Verify" clickable="true" enabled="true" bounds="[15,455][315,490]"/>
        <node package="com.facebook.katana" text="Not now" clickable="true" enabled="true" bounds="[15,495][315,530]"/>
        </node></hierarchy>
        """;

    [Fact]
    public async Task IdentityRestrictionAfterShareCancelsOnlySamePageAndRetainsUncertainUpload()
    {
        var current = await Job();
        var samePage = new PFlowJob
        {
            TargetId = current.TargetId,
            PostType = "Story",
            DeviceIndex = 9,
            State = PFlowState.Queued,
            Sha256 = current.Sha256,
            SourcePath = current.SourcePath,
        };
        var otherPage = new PFlowJob { TargetId = "another", State = PFlowState.Queued };
        var profile = new PFlowJob
        {
            TargetId = current.TargetId,
            IsProfile = true,
            State = PFlowState.Queued,
        };
        var done = new PFlowJob { TargetId = current.TargetId, State = PFlowState.Confirmed };
        var batch = new[] { current, samePage, otherPage, profile, done };
        Assert.True(await _store.ReserveAsync(samePage));
        var ld = FakeLd.Create(current);
        var fake = (FakeLd)(object)ld;
        fake.PostShareXml = PageIdentityXml;
        await new PFlowRunner(ld, _store, confirmationTimeout: TimeSpan.FromSeconds(2)).RunAsync(
            current,
            _ => Task.FromResult(true),
            async _ =>
            {
                foreach (var pending in PFlowPageIdentityRestriction.CancelQueued(current, batch))
                    await _store.SaveAsync(pending);
            },
            null,
            CancellationToken.None
        );

        Assert.True(current.TargetIdentityConfirmationRequired);
        Assert.False(PFlowPageIdentityRestriction.StopsWorker(current));
        Assert.True(
            PFlowPageIdentityRestriction.StopsWorker(new PFlowJob { State = PFlowState.Unknown })
        );
        Assert.Equal(PFlowState.Unknown, current.State);
        Assert.False(current.NoPublicationVerified);
        Assert.Equal(1, fake.ShareTaps);
        Assert.Equal(0, fake.Deletes);
        Assert.True(System.IO.File.Exists(current.SourcePath));
        Assert.Equal(PFlowState.CancelledBeforeSubmission, samePage.State);
        Assert.Equal(PFlowState.Queued, otherPage.State);
        Assert.Equal(PFlowState.Queued, profile.State);
        Assert.Equal(PFlowState.Confirmed, done.State);
        Assert.Empty(PFlowPageIdentityRestriction.CancelQueued(current, batch));
        var saved = await _store.ReadAsync();
        Assert.True(saved.Single(j => j.JobId == current.JobId).TargetIdentityConfirmationRequired);
        Assert.Equal(
            PFlowPageIdentityRestriction.Reason,
            saved.Single(j => j.JobId == samePage.JobId).Error
        );
    }

    [Theory]
    [InlineData("com.facebook.katana", "true", true)]
    [InlineData("other.package", "true", false)]
    [InlineData("com.facebook.katana", "false", false)]
    public void RequiresVisibleFacebookPageRestriction(
        string package,
        string visible,
        bool expected
    )
    {
        var xml = PageIdentityXml
            .Replace("com.facebook.katana", package)
            .Replace(
                "bounds=\"[0,0][330,580]\"",
                $"visible-to-user=\"{visible}\" bounds=\"[0,0][330,580]\""
            );
        Assert.Equal(expected, PFlowPageIdentityRestriction.IsVisible(xml));
        Assert.False(
            PFlowPageIdentityRestriction.IsVisible(
                xml.Replace("Before you can publish as the Page,", "A feed post says")
            )
        );
        Assert.False(
            PFlowPageIdentityRestriction.IsVisible(
                xml.Replace("Confirm your identity", "Other notification")
            )
        );
    }
}
