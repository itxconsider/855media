using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowJobStartRecoveryTests
{
    [Theory]
    [InlineData(PFlowState.Unknown, true)]
    [InlineData(PFlowState.Submitted, true)]
    [InlineData(PFlowState.SubmitRequested, true)]
    [InlineData(PFlowState.Failed, true)]
    [InlineData(PFlowState.Queued, false)]
    public void UnresolvedSubmissionBlocksNextJobAndRestartOnItsOwnLd(
        PFlowState state,
        bool blocked
    )
    {
        var job = new PFlowJob
        {
            DeviceIndex = 900799,
            TargetName = "Fixture Page",
            State = state,
            SubmittedUtc = state == PFlowState.Queued ? null : DateTime.UtcNow,
        };
        if (blocked)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                PFlowJobStartRecovery.RequireNoPendingPublication([job], 900799)
            );
            Assert.Contains(job.JobId, ex.Message);
            Assert.Contains("History", ex.Message);
        }
        else
            PFlowJobStartRecovery.RequireNoPendingPublication([job], 900799);
        PFlowJobStartRecovery.RequireNoPendingPublication([job], 900798);
        Assert.Equal(state, job.State);
    }

    [Fact]
    public void ExplicitlyReviewedFailedSubmissionCanProceedButOrdinaryFailureCannot()
    {
        var job = new PFlowJob
        {
            DeviceIndex = 900799,
            State = PFlowState.Failed,
            SubmittedUtc = DateTime.UtcNow,
        };
        Assert.Throws<InvalidOperationException>(() =>
            PFlowJobStartRecovery.RequireNoPendingPublication([job], 900799)
        );
        job.NoPublicationVerified = true;
        PFlowJobStartRecovery.RequireNoPendingPublication([job], 900799);
    }

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
    public async Task SlowStartupCanSettleAfterTheOldTwentyFrameLimit()
    {
        var (ld, device) = Create();
        device.Screens = new Queue<string>(
            Enumerable.Repeat(Unknown, 24).Concat(new[] { Feed, Feed })
        );
        var result = await PFlowJobStartRecovery.RecoverAsync(
            ld,
            5,
            "com.facebook.katana",
            null,
            Unknown,
            false,
            default,
            delay: NoDelay
        );
        Assert.True(result.Recovered);
        Assert.Equal(26, device.Reads);
        Assert.Equal(1, device.Opens);
    }

    [Fact]
    public async Task TransientFeedFrameDoesNotFinishRecovery()
    {
        var (ld, device) = Create();
        device.Screens = new Queue<string>(new[] { Feed, Unknown, Feed, Feed });
        await PFlowJobStartRecovery.RecoverAsync(
            ld,
            5,
            "com.facebook.katana",
            null,
            Unknown,
            false,
            default,
            delay: NoDelay
        );
        Assert.Equal(4, device.Reads);
        Assert.Equal(1, device.Opens);
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
        Assert.Equal(4, device.Reads);
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
        Assert.Equal(60, device.Reads);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("authentication")]
    [InlineData("input")]
    public async Task BlockingSecondFrameCannotCompleteRecovery(string kind)
    {
        var (ld, device) = Create();
        var blocked = kind switch
        {
            "foreign" => Unknown.Replace("com.facebook.katana", "another.app"),
            "authentication" =>
                "<hierarchy><node package='com.facebook.katana' text='Wrong Credentials'/><node package='com.facebook.katana' text='Invalid username or password'/></hierarchy>",
            _ =>
                "<hierarchy><node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>",
        };
        device.Screens = new Queue<string>(new[] { Feed, blocked });
        device.Fallback = blocked;
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
        Assert.Equal(kind == "input" ? 60 : 2, device.Reads);
    }

    [Fact]
    public async Task CancellationAfterFirstFeedFrameStopsWithoutAnotherNavigation()
    {
        var (ld, device) = Create();
        using var stop = new CancellationTokenSource();
        var waits = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowJobStartRecovery.RecoverAsync(
                ld,
                5,
                "com.facebook.katana",
                null,
                Unknown,
                false,
                stop.Token,
                delay: (_, token) =>
                {
                    if (++waits == 2)
                        stop.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(1, device.Reads);
        Assert.Equal(1, device.Opens);
    }

    [Fact]
    public async Task ForegroundLossDuringFinalHierarchyReadCannotFinishRecovery()
    {
        var (ld, device) = Create();
        device.OnRead = count =>
        {
            if (count == 2)
                device.Activity = "another.app/Activity";
        };
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
        Assert.Equal(2, device.Reads);
        Assert.Equal(1, device.Opens);
    }

    [Fact]
    public async Task CapturedLd20FeedIsRecognizedWithoutRecovery()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("ld20-feed-startup-captured");
        Assert.True(FacebookLayoutRegistry.Match(xml, FacebookScreenKind.NewsFeed).IsMatch);
        var (ld, device) = Create();
        Assert.False(
            (
                await PFlowJobStartRecovery.RecoverAsync(
                    ld,
                    20,
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
        public Action<int>? OnRead;

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
                OnRead?.Invoke(Reads);
                return Task.FromResult<string?>(Screens.Count > 0 ? Screens.Dequeue() : Fallback);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
