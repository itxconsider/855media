using System;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    [Fact]
    public async Task PublicationFailureTakesPrecedenceOverSuccessText()
    {
        var job = await Job();
        var ld = FakeLd.Create(job);
        var device = (FakeLd)(object)ld;
        device.PostShareXml =
            "<hierarchy><node package='com.facebook.katana' text='Upload failed'/><node text='Reel published'/></hierarchy>";
        await new PFlowRunner(ld, _store, confirmationTimeout: TimeSpan.FromSeconds(2)).RunAsync(
            job,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(PFlowState.Unknown, job.State);
        Assert.Null(job.ConfirmedUtc);
        Assert.Equal(0, device.Deletes);
        Assert.Equal(1, device.ShareTaps);
    }

    [Fact]
    public async Task DirectConfirmationWaitsForSuccessAfterUploadNotification()
    {
        var job = await Job();
        var ld = FakeLd.Create(job);
        var device = (FakeLd)(object)ld;
        device.NotificationSuccess = true;
        device.PostShareSequence.Enqueue("<hierarchy><node text='Home'/></hierarchy>");
        device.PostShareSequence.Enqueue("<hierarchy><node text='Home'/></hierarchy>");
        device.PostShareXml =
            "<hierarchy><node package='com.facebook.katana' text='Your reel has been published!'/></hierarchy>";
        await new PFlowRunner(ld, _store, confirmationTimeout: TimeSpan.FromSeconds(4)).RunAsync(
            job,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            CancellationToken.None
        );
        Assert.Equal(PFlowState.Confirmed, job.State);
        Assert.Equal("Your reel has been published!", job.Evidence);
        Assert.False(job.ProfilePublicationCheckRequired);
        Assert.Equal(1, device.ShareTaps);
        Assert.NotNull(job.ConfirmedUtc);
    }
}
