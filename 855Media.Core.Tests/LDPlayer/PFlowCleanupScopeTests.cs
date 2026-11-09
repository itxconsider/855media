using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowCleanupScopeTests
{
    [Theory]
    [InlineData("ld9", "ld14", true)]
    [InlineData("ld14", "ld14", false)]
    [InlineData("replaced-instance", "ld14", true)]
    [InlineData("", "ld14", false)]
    public void InstanceIdentityNotIndexDeterminesDeferredCleanup(
        string saved,
        string current,
        bool defer
    )
    {
        var job = new PFlowJob
        {
            DeviceIndex = 45,
            AccountBinding = new(saved, 45, "LD-45", "com.facebook.katana", "123"),
        };
        Assert.Equal(defer, PFlowCleanupScope.IsDifferentInstance(job, current));
        Assert.Equal("Pending", job.RemoteCleanupState);
    }

    [Fact]
    public void MissingOrInconsistentBindingRetainsExistingGuardedCleanup()
    {
        var job = new PFlowJob { DeviceIndex = 45 };
        Assert.False(PFlowCleanupScope.IsDifferentInstance(job, "ld14"));
        job.AccountBinding = new("ld9", 46, "LD-46", "com.facebook.katana", "123");
        Assert.False(PFlowCleanupScope.IsDifferentInstance(job, "ld14"));
    }

    [Fact]
    public void MissingCurrentIdentityCannotSilentlyBypassCleanup()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PFlowCleanupScope.IsDifferentInstance(new(), "")
        );
    }
}
