using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed partial class PFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReelShareOpensHomeAndRetainsFreshSuccessButFreshErrorsWin(bool error)
    {
        var job = await Job();
        var ld = FakeLd.Create(job);
        var fake = (FakeLd)(object)ld;
        fake.PostShareXml =
            "<hierarchy><node package='com.facebook.katana' text='Reel published'/></hierarchy>";
        fake.PostHomeXml = error
            ? "<hierarchy><node package='com.facebook.katana' text='Unable to publish'/></hierarchy>"
            : "<hierarchy><node package='com.facebook.katana' text='Home'/></hierarchy>";
        var confirmed = await new PFlowRunner(ld, _store).RunAsync(
            job,
            _ => Task.FromResult(true),
            _ => Task.CompletedTask,
            null,
            default
        );
        Assert.Equal(!error, confirmed);
        Assert.Equal(error ? PFlowState.Failed : PFlowState.Confirmed, job.State);
        Assert.Equal(1, fake.ShareTaps);
        Assert.Equal(1, fake.HomeNavigations);
    }
}
