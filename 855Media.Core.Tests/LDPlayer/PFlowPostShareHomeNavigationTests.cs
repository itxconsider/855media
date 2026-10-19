using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowPostShareHomeNavigationTests
{
    private const string Pending =
        "<hierarchy><node package='com.facebook.katana' text='Uploading reel...'/></hierarchy>";

    private static PFlowJob Submitted() =>
        new() { State = PFlowState.Submitted, SubmittedUtc = DateTime.UtcNow };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HomeIsRequestedOnceAfterSubmissionEvenWhileUploadIsPending(bool acknowledged)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Acknowledged = acknowledged;
        var navigation = new PFlowPostShareHomeNavigation();
        var job = Submitted();
        Assert.True(
            await navigation.TryAsync(ld, job, Pending, null, default, (_, _) => Task.CompletedTask)
        );
        Assert.False(await navigation.TryAsync(ld, job, Pending, null, default));
        Assert.Equal(1, device.HomeNavigations);
        Assert.Equal(PFlowState.Submitted, job.State);
        Assert.Null(job.ConfirmedUtc);
        Assert.True(PFlowConfirmation.HasPendingPublication(Pending));
    }

    [Theory]
    [InlineData(PFlowState.ComposerReady)]
    [InlineData(PFlowState.SubmitRequested)]
    [InlineData(PFlowState.CancelledBeforeSubmission)]
    [InlineData(PFlowState.Unknown)]
    public void NoHomeNavigationBeforeAcknowledgedSubmission(PFlowState state)
    {
        var job = Submitted();
        job.State = state;
        Assert.False(new PFlowPostShareHomeNavigation().CanNavigate(job, Pending));
    }

    [Theory]
    [InlineData(
        "<hierarchy><node package='com.facebook.katana' text='Reel Settings'/><node package='com.facebook.katana' text='Share now'/></hierarchy>"
    )]
    [InlineData(
        "<hierarchy><node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
    )]
    [InlineData("<hierarchy><node package='com.other.app' text='Uploading reel...'/></hierarchy>")]
    [InlineData("<hierarchy/>")]
    public void WaitsForComposerExitAndUsableFacebookObservation(string xml) =>
        Assert.False(new PFlowPostShareHomeNavigation().CanNavigate(Submitted(), xml));

    [Fact]
    public async Task AlreadyVisibleHomeNeedsNoDeepLinkAndCancellationSendsNothing()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld1-feed-top.xml"
            )
        );
        Assert.False(
            await new PFlowPostShareHomeNavigation().TryAsync(ld, Submitted(), xml, null, default)
        );
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowPostShareHomeNavigation().TryAsync(
                ld,
                Submitted(),
                Pending,
                null,
                cancel.Token
            )
        );
        Assert.Equal(0, device.HomeNavigations);
    }

    public class Device : DispatchProxy
    {
        public int HomeNavigations;
        public bool Acknowledged;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("OpenDeepLinkAsync", method!.Name);
            Assert.Equal("fb://feed", args![1]);
            Assert.Equal("com.facebook.katana", args[2]);
            HomeNavigations++;
            return Task.FromResult(Acknowledged);
        }
    }
}
