using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowJobStartRecoveryTests
{
    private const string Unknown =
        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]' text='Unrecognized starting screen'/></hierarchy>";
    private static string Feed =>
        FacebookLayoutRegistryTests.Fixture("ld2-feed-after-kc-waterheaters-confirmed");

    private static Task NoDelay(TimeSpan _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static (ILDPlayerService Ld, Device Device) Create()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    [Fact]
    public async Task UnknownStartReturnsToRecognizedFeedOnceAndStillRequiresFreshUid()
    {
        var (ld, device) = Create();
        device.Screens = new Queue<string>(new[] { Unknown, "<hierarchy/>", Feed });
        var job = new PFlowJob
        {
            DeviceIndex = 5,
            TargetName = "Target",
            State = PFlowState.Reserved,
        };
        var result = await PFlowJobStartRecovery.RecoverAsync(
            ld,
            5,
            "com.facebook.katana",
            null,
            Unknown,
            false,
            default,
            job,
            delay: NoDelay
        );
        Assert.True(result.Recovered);
        Assert.Equal(1, device.Opens);
        Assert.Equal(3, device.Reads);
        Assert.Equal(PFlowState.Reserved, job.State);
        Assert.Null(job.TargetContinuation);
        var uidReads = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowPostTargetSession().VerifyAsync(
                job,
                _ =>
                {
                    uidReads++;
                    return Task.FromResult("Wrong UID");
                },
                _ => Task.FromResult(true),
                _ => Task.FromResult(result.Xml),
                default,
                initialXml: result.Xml,
                allowFeedAfterLiveUid: true
            )
        );
        Assert.Equal(1, uidReads);
    }

    [Fact]
    public async Task KnownFeedDoesNotNavigateOrRefreshUnnecessarily()
    {
        var (ld, device) = Create();
        Assert.False(
            (
                await PFlowJobStartRecovery.RecoverAsync(
                    ld,
                    5,
                    "com.facebook.katana",
                    null,
                    Feed,
                    false,
                    default,
                    delay: NoDelay
                )
            ).Recovered
        );
        Assert.Equal(0, device.Opens + device.Reads + device.FocusChecks);
    }

    [Theory]
    [InlineData(PFlowState.SubmitRequested)]
    [InlineData(PFlowState.Submitted)]
    [InlineData(PFlowState.Unknown)]
    [InlineData(PFlowState.Confirmed)]
    public async Task SubmittedAndUncertainJobsNeverReturnToFeed(PFlowState state)
    {
        var (ld, device) = Create();
        var job = new PFlowJob { DeviceIndex = 5, State = state };
        Assert.False(
            (
                await PFlowJobStartRecovery.RecoverAsync(
                    ld,
                    5,
                    "com.facebook.katana",
                    null,
                    Unknown,
                    false,
                    default,
                    job,
                    delay: NoDelay
                )
            ).Recovered
        );
        Assert.Equal(0, device.Opens + device.Reads + device.FocusChecks);
    }

    [Fact]
    public async Task PendingDeviceHistoryAndSubmissionTimestampPreventRecovery()
    {
        var (ld, device) = Create();
        Assert.True(
            PFlowJobStartRecovery.HasPendingPublication(
                new[]
                {
                    new PFlowJob { DeviceIndex = 5, State = PFlowState.Unknown },
                },
                5
            )
        );
        Assert.False(
            PFlowJobStartRecovery.HasPendingPublication(
                new[]
                {
                    new PFlowJob { DeviceIndex = 6, State = PFlowState.Unknown },
                },
                5
            )
        );
        await PFlowJobStartRecovery.RecoverAsync(
            ld,
            5,
            "com.facebook.katana",
            null,
            Unknown,
            true,
            default,
            delay: NoDelay
        );
        await PFlowJobStartRecovery.RecoverAsync(
            ld,
            5,
            "com.facebook.katana",
            null,
            Unknown,
            false,
            default,
            new PFlowJob { State = PFlowState.Failed, SubmittedUtc = DateTime.UtcNow },
            delay: NoDelay
        );
        Assert.Equal(0, device.Opens + device.Reads + device.FocusChecks);
    }

    [Fact]
    public async Task ForeignAuthenticationAndEditableStartingScreensAreNotDismissed()
    {
        foreach (
            var xml in new[]
            {
                Unknown.Replace("com.facebook.katana", "another.app"),
                Unknown.Replace("text='Unrecognized starting screen'", "password='true'"),
                Unknown.Replace(
                    "text='Unrecognized starting screen'",
                    "class='android.widget.EditText'"
                ),
            }
        )
        {
            var (ld, device) = Create();
            Assert.False(
                (
                    await PFlowJobStartRecovery.RecoverAsync(
                        ld,
                        5,
                        "com.facebook.katana",
                        null,
                        xml,
                        false,
                        default,
                        delay: NoDelay
                    )
                ).Recovered
            );
            Assert.Equal(0, device.Opens + device.Reads + device.FocusChecks);
        }
        Assert.True(
            PFlowJobStartRecovery.NeedsRecovery(
                MetaAccountLayoutTests.Settings(),
                "com.facebook.katana"
            )
        );
    }

    [Fact]
    public async Task RecoveryTimesOutWithoutRetappingOrAcceptingAnUnknownScreen()
    {
        var (ld, device) = Create();
        device.Fallback = Unknown;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowJobStartRecovery.RecoverAsync(
                ld,
                5,
                "com.facebook.katana",
                null,
                Unknown,
                false,
                default,
                delay: NoDelay
            )
        );
        Assert.Equal(1, device.Opens);
        Assert.Equal(20, device.Reads);
    }

    [Fact]
    public async Task ForegroundChangeAndCancellationStopRecovery()
    {
        var (ld, device) = Create();
        device.Activity = "another.app/Activity";
        Assert.False(
            (
                await PFlowJobStartRecovery.RecoverAsync(
                    ld,
                    5,
                    "com.facebook.katana",
                    null,
                    Unknown,
                    false,
                    default,
                    delay: NoDelay
                )
            ).Recovered
        );
        Assert.Equal(0, device.Opens);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowJobStartRecovery.RecoverAsync(
                ld,
                5,
                "com.facebook.katana",
                null,
                Unknown,
                false,
                cancel.Token,
                delay: NoDelay
            )
        );
    }

    public class Device : DispatchProxy
    {
        public int Opens,
            Reads,
            FocusChecks;
        public string Activity = "com.facebook.katana/MainActivity";
        public string Fallback = Feed;
        public Queue<string> Screens = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "GetCurrentFocusedActivityAsync")
            {
                FocusChecks++;
                return Task.FromResult<string?>(Activity);
            }
            if (method.Name == "OpenDeepLinkAsync")
            {
                Assert.Equal("fb://feed", args![1]);
                Assert.Equal("com.facebook.katana", args[2]);
                Opens++;
                return Task.FromResult(true);
            }
            if (method.Name == "DumpUiHierarchyAsync")
            {
                Reads++;
                return Task.FromResult<string?>(Screens.Count > 0 ? Screens.Dequeue() : Fallback);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
