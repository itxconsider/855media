using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPostTargetSessionTests
{
    [Theory]
    [InlineData("ld2-feed-after-john-ward-confirmed")]
    [InlineData("ld2-feed-after-kc-waterheaters-confirmed")]
    public async Task ConfirmedSameTargetContinuesCapturedHomeWithoutAnotherUidOrProfileLookup(
        string fixture
    )
    {
        var h = new Harness();
        var previous = Job();
        await h.Verify(previous);
        Confirm(previous);
        h.Xml = FacebookLayoutRegistryTests.Fixture(fixture);
        var uidReads = h.UidReads;
        var opens = h.Opens;
        var next = Job();
        Assert.True(await h.Verify(next));
        Assert.Equal(uidReads, h.UidReads);
        Assert.Equal(opens, h.Opens);
        next.State = PFlowState.IdentityVerified;
        Assert.True(PFlowPostTargetSession.ConsumeContinuation(next, h.Xml));
    }

    [Theory]
    [InlineData("Graph UID")]
    [InlineData("Wrong UID")]
    public async Task ExplicitRecoveryAcceptsVerifiedUidAndHomeButNeverAMismatchedUid(string method)
    {
        var session = new PFlowPostTargetSession();
        var job = Job();
        var xml = FacebookLayoutRegistryTests.Fixture("ld2-feed-after-kc-waterheaters-confirmed");
        var opens = 0;
        Task<bool> Verify() =>
            session.VerifyAsync(
                job,
                _ => Task.FromResult(method),
                _ =>
                {
                    opens++;
                    return Task.FromResult(false);
                },
                _ => Task.FromResult(xml),
                default,
                xml,
                allowFeedAfterLiveUid: true
            );
        if (method == "Wrong UID")
            await Assert.ThrowsAsync<InvalidOperationException>(Verify);
        else
        {
            Assert.True(await Verify());
            job.State = PFlowState.IdentityVerified;
            Assert.True(PFlowPostTargetSession.ConsumeContinuation(job, xml));
        }
        Assert.Equal(0, opens);
    }

    internal const string Feed =
        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node text='Home'/></node></hierarchy>";

    internal static string Own(PFlowJob job) =>
        $"<hierarchy><node package='com.facebook.katana' text='{job.TargetName}'/><node package='com.facebook.katana' text='Professional dashboard'/><node package='com.facebook.katana' text='Edit profile'/></hierarchy>";

    internal static PFlowJob Job() =>
        new()
        {
            DeviceIndex = 35,
            BatchId = "batch",
            OwnerUid = "22222",
            TargetId = "11111",
            PersonaUid = "33333",
            TargetName = "Target",
            AccountBinding = new("instance", 35, "LD-35", "com.facebook.katana", "22222"),
        };

    internal static void Confirm(PFlowJob job)
    {
        job.State = PFlowState.Confirmed;
        job.ConfirmedUtc = DateTime.UtcNow;
        job.Evidence = "Fresh publication confirmation";
    }

    private sealed class Harness
    {
        internal readonly PFlowPostTargetSession Session = new();
        internal int UidReads,
            Opens,
            Captures;
        internal string Xml = Feed,
            Method = "Graph UID";
        internal bool FailUid;
        internal Action? DuringUid;

        internal Task<bool> Verify(PFlowJob job, string? initialXml = null) =>
            Session.VerifyAsync(
                job,
                _ =>
                {
                    UidReads++;
                    DuringUid?.Invoke();
                    if (FailUid)
                        throw new InvalidOperationException("Live UID mismatch");
                    return Task.FromResult(Method);
                },
                _ =>
                {
                    Opens++;
                    Xml = Own(job);
                    return Task.FromResult(true);
                },
                _ =>
                {
                    Captures++;
                    return Task.FromResult(Xml);
                },
                default,
                initialXml
            );

        internal async Task<PFlowJob> ConfirmFirst()
        {
            var first = Job();
            Assert.True(await Verify(first));
            Confirm(first);
            Xml = Feed;
            return first;
        }
    }

    [Fact]
    public async Task SwitchingLoaderInvalidatesContinuationAndRequiresFreshUid()
    {
        var h = new Harness();
        await h.ConfirmFirst();
        var next = Job();
        var loader = FacebookLayoutRegistryTests.Fixture("identity-switching-screenshot");
        Assert.False(PFlowPostTargetSession.HasOwnProfile(next, loader));
        await h.Verify(next, loader);
        Assert.Equal(2, h.UidReads);
        Assert.Equal(2, h.Opens);
        Assert.Null(next.TargetContinuation);
    }

    [Fact]
    public async Task ConsecutivePostReusesTheCallersFreshCaptureBeforeAnyNavigation()
    {
        var h = new Harness();
        await h.ConfirmFirst();
        Assert.Equal(1, h.Captures); // Only the post-navigation profile capture.
        var next = Job();
        await h.Verify(next, Feed);
        Assert.Equal(1, h.Captures);
        Assert.Equal(1, h.UidReads);
        next.State = PFlowState.IdentityVerified;
        Assert.True(PFlowPostTargetSession.ConsumeContinuation(next, Feed));
    }

    [Fact]
    public async Task TenPostsReuseOneUidVerificationAcrossPostTypes()
    {
        var h = new Harness();
        await h.ConfirmFirst();
        for (var i = 1; i < 10; i++)
        {
            var job = Job();
            job.PostType = i % 2 == 0 ? "Story" : "Reel";
            await h.Verify(job);
            job.State = PFlowState.IdentityVerified;
            Assert.True(PFlowPostTargetSession.ConsumeContinuation(job, Feed));
            Confirm(job);
        }
        Assert.Equal(1, h.UidReads);
        Assert.Equal(1, h.Opens);
    }

    [Fact]
    public async Task ConsecutiveConfirmedPostsReuseUidAndOpenProfileOnlyOnce()
    {
        var h = new Harness();
        await h.ConfirmFirst();
        var second = Job();
        Assert.True(await h.Verify(second));
        Assert.Equal(1, h.UidReads);
        Assert.Equal(1, h.Opens);
        second.State = PFlowState.IdentityVerified;
        Assert.True(PFlowPostTargetSession.ConsumeContinuation(second, Feed));
        Assert.False(PFlowPostTargetSession.ConsumeContinuation(second, Feed));
    }

    [Theory]
    [InlineData(PFlowState.Unknown)]
    [InlineData(PFlowState.Failed)]
    [InlineData(PFlowState.CancelledBeforeSubmission)]
    [InlineData(PFlowState.Submitted)]
    public async Task UnconfirmedPreviousPostCannotSupplyContinuation(PFlowState state)
    {
        var h = new Harness();
        var first = await h.ConfirmFirst();
        first.State = state;
        var next = Job();
        await h.Verify(next);
        Assert.Equal(2, h.Opens);
        Assert.Equal(2, h.UidReads);
        Assert.Null(next.TargetContinuation);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("owner")]
    [InlineData("persona")]
    [InlineData("name")]
    [InlineData("device")]
    [InlineData("batch")]
    [InlineData("binding")]
    [InlineData("profile")]
    public async Task ChangedRoutingOrStoryRequiresOwnProfile(string change)
    {
        var h = new Harness();
        await h.ConfirmFirst();
        var next = Job();
        switch (change)
        {
            case "target":
                next.TargetId = "44444";
                break;
            case "owner":
                next.OwnerUid = "44444";
                break;
            case "persona":
                next.PersonaUid = "44444";
                break;
            case "name":
                next.TargetName = "Other";
                break;
            case "device":
                next.DeviceIndex = 36;
                break;
            case "batch":
                next.BatchId = "new-batch";
                break;
            case "binding":
                next.AccountBinding = next.AccountBinding! with
                {
                    InstanceIdentity = "replacement",
                };
                break;
            case "profile":
                next.IsProfile = true;
                break;
            case "story":
                next.PostType = "Story";
                break;
        }
        await h.Verify(next);
        Assert.Equal(2, h.Opens);
        Assert.Equal(2, h.UidReads);
        Assert.Null(next.TargetContinuation);
    }

    [Fact]
    public async Task FailedFreshUidDoesNotFallBackToThePreviousPost()
    {
        var h = new Harness();
        await h.ConfirmFirst();
        h.Session.Invalidate();
        h.FailUid = true;
        var next = Job();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Verify(next));
        Assert.Null(next.TargetContinuation);
        Assert.Equal(1, h.Opens);
        h.FailUid = false;
        await h.Verify(Job());
        Assert.Equal(2, h.Opens);
    }

    [Theory]
    [InlineData("UI UID lookup")]
    [InlineData("")]
    public async Task MissingGraphRequiresFreshUiProofOrStops(string method)
    {
        var h = new Harness();
        await h.ConfirmFirst();
        h.Session.Invalidate();
        h.Method = method;
        var next = Job();
        if (method.Length == 0)
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.Verify(next));
        else
        {
            await h.Verify(next);
            Assert.Equal(2, h.Opens);
        }
        Assert.Null(next.TargetContinuation);
    }

    [Fact]
    public async Task AccountSelectionDuringUidVerificationInvalidatesContinuation()
    {
        var h = new Harness();
        await h.ConfirmFirst();
        h.Session.Invalidate();
        h.DuringUid = h.Session.Invalidate;
        var next = Job();
        await h.Verify(next);
        Assert.Equal(2, h.Opens);
        Assert.Equal(2, h.UidReads);
        Assert.Null(next.TargetContinuation);
    }

    [Theory]
    [InlineData("<hierarchy/>")]
    [InlineData("<hierarchy><node package='other.app'/></hierarchy>")]
    [InlineData(
        "<hierarchy><node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
    )]
    [InlineData(
        "<hierarchy><node package='com.facebook.katana' text='Reel Settings'/></hierarchy>"
    )]
    public async Task MissingOrUnsafeSurfaceUsesFullProfileRoute(string xml)
    {
        var h = new Harness();
        await h.ConfirmFirst();
        h.Xml = xml;
        var next = Job();
        await h.Verify(next);
        Assert.Equal(2, h.Opens);
        Assert.Equal(2, h.UidReads);
        Assert.Null(next.TargetContinuation);
    }

    [Fact]
    public async Task ContinuationIsNotPersistedOrUsableAfterTargetChangeOrExpiry()
    {
        var h = new Harness();
        await h.ConfirmFirst();
        var job = Job();
        await h.Verify(job);
        Assert.Null(
            JsonSerializer.Deserialize<PFlowJob>(JsonSerializer.Serialize(job))!.TargetContinuation
        );
        var proof = job.TargetContinuation!;
        job.State = PFlowState.IdentityVerified;
        job.TargetContinuation = proof with { VerifiedUtc = DateTime.UtcNow.AddMinutes(-2) };
        Assert.False(PFlowPostTargetSession.ConsumeContinuation(job, Feed));
        job.TargetContinuation = proof;
        job.PersonaUid = "44444";
        Assert.False(PFlowPostTargetSession.ConsumeContinuation(job, Feed));
    }

    [Fact]
    public async Task FailedProfileNavigationReportsUidSuccessSeparately()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowPostTargetSession().VerifyAsync(
                Job(),
                _ => Task.FromResult("Graph UID"),
                _ => Task.FromResult(false),
                _ => Task.FromResult(Feed),
                default
            )
        );
        Assert.Contains("Live UID was verified", error.Message);
        Assert.Contains("profile screen", error.Message);
    }
}
