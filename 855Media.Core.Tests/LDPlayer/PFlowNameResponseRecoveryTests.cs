using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowNameResponseRecoveryTests
{
    [Fact]
    public async Task UploadThatClearsIntoStalledLoaderGetsOneGuardedRecovery()
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        var guards = 0;
        var proof = await h.Run(
            _ =>
            {
                guards++;
                return Task.CompletedTask;
            },
            onDelay: () =>
            {
                if (h.Fake.Now >= DateTimeOffset.UnixEpoch.AddSeconds(2))
                    h.Fake.Loading = White;
            }
        );
        Assert.True(proof.UsesDatabaseName);
        Assert.Equal(1, h.Fake.Stops);
        Assert.Equal(1, h.Fake.Launches);
        Assert.True(guards >= 1);
        Assert.Equal(0, h.Fake.ForbiddenReads);
    }

    [Fact]
    public async Task ApprovedBackgroundScanIsReobservedBeforeStrictNameReading()
    {
        var h = new Harness();
        var target = Target();
        h.Fake.Loading = Header()
            .Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' text='Running copyright scan…'/></hierarchy>"
            );
        var approvals = 0;
        var proof = await PFlowPostingNameVerification.ActivateWithResponseWaitAsync(
            h.Ld,
            Binding,
            target,
            [target],
            null,
            default,
            null,
            (duration, _) =>
            {
                h.Fake.Now += duration;
                return Task.CompletedTask;
            },
            () => h.Fake.Now,
            confirmedBackgroundObservation: (xml, _) =>
            {
                approvals++;
                Assert.True(PFlowBackgroundCopyright.IsOnlyBackgroundScan(xml));
                return Task.FromResult<string?>(Header());
            }
        );
        Assert.True(proof.UsesDatabaseName);
        Assert.True(approvals > 0);
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
    }

    [Theory]
    [InlineData("settles")]
    [InlineData("timeout")]
    [InlineData("process")]
    [InlineData("foreign")]
    [InlineData("cancel")]
    [InlineData("input")]
    public async Task CapturedPostSelectionSheetWaitsWithoutActions(string mode)
    {
        var h = new Harness();
        h.Fake.Loading = FacebookLayoutRegistryTests.Fixture("ld56-switcher-after-selection");
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(h.Fake.Loading));
        using var stop = new CancellationTokenSource();
        var polls = 0;
        var task = PFlowPostingNameVerification.WaitForSelectedSwitcherCloseAsync(
            h.Ld,
            Binding,
            null,
            stop.Token,
            (duration, ct) =>
            {
                h.Fake.Now += duration;
                polls++;
                if (mode == "settles" && polls == 3)
                    h.Fake.Loading = Header();
                if (mode == "process")
                    h.Fake.Process = "999";
                if (mode == "foreign")
                    h.Fake.Activity = "other.app/Main";
                if (mode == "cancel")
                    stop.Cancel();
                if (mode == "input")
                    h.Fake.Loading = h.Fake.Loading.Replace(
                        "</hierarchy>",
                        "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
                    );
                return Task.CompletedTask;
            },
            () => h.Fake.Now
        );
        if (mode == "settles")
        {
            await task;
            Assert.Equal(3, polls);
        }
        else if (mode == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            if (mode == "timeout")
                Assert.Contains("did not close", error.Message);
        }
        Assert.InRange(polls, 1, 30);
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
    }

    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        900757,
        "fixture-LD",
        "com.facebook.katana",
        "100000000000001"
    );

    private static FacebookPageTarget Target() =>
        new()
        {
            PageName = "Fixture Page",
            Type = "Page",
            PageId = "100000000000002",
            UserUid = Binding.OwnerUid,
            AccountBinding = Binding,
        };

    private const string White =
        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'/></hierarchy>";
    private const string Menu =
        "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'>"
        + "<node content-desc='Close menu.'/><node text='Menu'/><node text='Background feed item'/></node></hierarchy>";

    private static string Header(string name = "Fixture Page") =>
        White.Replace(
            "</hierarchy>",
            "<node package='com.facebook.katana' content-desc='"
                + name
                + ", see your profile' bounds='[48,108][405,188]'/></hierarchy>"
        );

    private static string PendingHeader() =>
        Header()
            .Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' content-desc='Finishing reel upload. Keep Facebook open.'/></hierarchy>"
            );

    private sealed class Harness
    {
        public ILDPlayerService Ld { get; } = DispatchProxy.Create<ILDPlayerService, Device>();
        public Device Fake => (Device)(object)Ld;
        public List<string> Progress = [];
        public string TargetName = "Fixture Page";

        public Task<PFlowNativeIdentityScope> Run(
            Func<CancellationToken, Task>? idle = null,
            CancellationToken ct = default,
            Action? onDelay = null
        )
        {
            var target = Target();
            target.PageName = TargetName;
            return PFlowPostingNameVerification.ActivateWithResponseWaitAsync(
                Ld,
                Binding,
                target,
                [target],
                null,
                ct,
                idle,
                (duration, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Fake.Now += duration;
                    onDelay?.Invoke();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                () => Fake.Now,
                m =>
                {
                    Progress.Add(m);
                    return Task.CompletedTask;
                }
            );
        }
    }

    [Theory]
    [InlineData("white")]
    [InlineData("empty")]
    [InlineData("menu")]
    public async Task SlowResponseIsWaitedForWithoutRestartingOrReadingGraph(string frame)
    {
        var h = new Harness();
        h.Fake.Loading =
            frame == "empty" ? ""
            : frame == "menu" ? Menu
            : White;
        h.Fake.ReadyAt = h.Fake.Now.AddSeconds(25);
        var scope = await h.Run(_ => Task.CompletedTask);
        Assert.True(scope.UsesDatabaseName);
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
        Assert.True(h.Fake.Now >= h.Fake.ReadyAt);
    }

    [Fact]
    public async Task PostSelectionWhiteScreenGetsOneGuardedRestartAndFreshProcessProof()
    {
        var h = new Harness();
        h.Fake.FirstHeader = true;
        var guards = 0;
        var scope = await h.Run(_ =>
        {
            guards++;
            return Task.CompletedTask;
        });
        Assert.Equal(1, guards);
        Assert.Equal(1, h.Fake.Stops);
        Assert.Equal(1, h.Fake.Launches);
        Assert.True(scope.Matches(Binding, "456", Target()));
        Assert.Equal(0, h.Fake.ForbiddenReads);
    }

    [Fact]
    public async Task StillLoadingAfterRestartStopsAfterTheSecondWait()
    {
        var h = new Harness();
        h.Fake.Recover = false;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            h.Run(_ => Task.CompletedTask)
        );
        Assert.Equal(1, h.Fake.Stops);
        Assert.Equal(1, h.Fake.Launches);
        Assert.InRange((h.Fake.Now - DateTimeOffset.UnixEpoch).TotalSeconds, 120, 123);
    }

    [Fact]
    public async Task NoRestartWithoutCallersPendingWorkGuard()
    {
        var h = new Harness();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => h.Run());
        Assert.Equal(0, h.Fake.Stops);
    }

    [Fact]
    public async Task PendingOrUncertainHistoryBlocksRestart()
    {
        var h = new Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Run(_ => throw new InvalidOperationException("Pending publication"))
        );
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("unknown-notifications")]
    [InlineData("foreground-change")]
    public async Task FreshBoundaryRechecksBlockUnsafeRestart(string change)
    {
        var h = new Harness();
        if (change == "upload")
            h.Fake.Notifications = PFlowConfirmationTests.Dump(
                title: "Uploading your reel",
                flags: "2"
            );
        if (change == "unknown-notifications")
            h.Fake.Notifications = "unsupported";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Run(_ =>
            {
                if (change == "foreground-change")
                    h.Fake.Activity = "another.app/Main";
                return Task.CompletedTask;
            })
        );
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("pending")]
    [InlineData("foreign")]
    [InlineData("wrong-name")]
    public async Task UnsafeOrContradictoryScreensNeverTriggerLoadingRestart(string change)
    {
        var h = new Harness();
        h.Fake.FirstHeader = true;
        h.Fake.Loading = change switch
        {
            "input" => White.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
            ),
            "pending" => White.Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' text='Uploading your reel'/></hierarchy>"
            ),
            "foreign" => White.Replace("com.facebook.katana", "another.app"),
            _ => Header("Wrong Actor"),
        };
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            h.Run(_ => Task.CompletedTask)
        );
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
    }

    [Fact]
    public async Task AmbiguousMenuHeadersAreNotTreatedAsBlankLoading()
    {
        var h = new Harness();
        h.Fake.FirstHeader = true;
        h.Fake.Loading = Menu.Replace(
            "</hierarchy>",
            "<node package='com.facebook.katana' content-desc='First Actor, see your profile' bounds='[48,108][405,150]'/>"
                + "<node package='com.facebook.katana' content-desc='Second Actor, see your profile' bounds='[48,150][405,188]'/></hierarchy>"
        );
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            h.Run(_ => Task.CompletedTask)
        );
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    [Fact]
    public async Task CancellationDuringLoadingDoesNotRestart()
    {
        var h = new Harness();
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Run(_ => Task.CompletedTask, stop.Token, () => stop.Cancel())
        );
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    [Fact]
    public async Task LateHeaderAtRecoveryBoundaryAvoidsRestart()
    {
        var h = new Harness();
        var scope = await h.Run(_ =>
        {
            h.Fake.ReadyAt = h.Fake.Now;
            return Task.CompletedTask;
        });
        Assert.True(scope.UsesDatabaseName);
        Assert.Equal(0, h.Fake.Stops);
    }

    [Fact]
    public async Task NativeSelectionCannotSpendAnotherRestartWhenOuterCallerOwnsTheBudget()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowSharedSwitchTests.SwitchDevice>();
        var device = (PFlowSharedSwitchTests.SwitchDevice)(object)ld;
        device.State = "hidden";
        device.FailReveal = true;
        var now = DateTime.UtcNow;
        var service = new PFlowPersonaSwitcherService(
            ld,
            null,
            (span, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                now += span;
                return Task.CompletedTask;
            },
            () => now
        );
        Assert.False(
            await service.SelectNativeTargetAsync(
                900757,
                new FacebookPageTarget
                {
                    Type = "Profile",
                    PageName = "Gerardo Lopez",
                    PageId = "100008345077125",
                },
                null,
                default,
                postingNameOnly: true,
                restartOnNavigationFailure: false
            )
        );
        Assert.Equal(0, device.Stops + device.Launches);
    }

    [Fact]
    public async Task UnacknowledgedStopDoesNotLaunchOrRetryAgain()
    {
        var h = new Harness();
        h.Fake.StopOk = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Run(_ => Task.CompletedTask));
        Assert.Equal(1, h.Fake.Stops);
        Assert.Equal(0, h.Fake.Launches);
    }

    [Fact]
    public async Task PreviousFinishingUploadSettlesWithoutNavigationOrRestart()
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        h.Fake.ReadyAt = h.Fake.Now.AddSeconds(5);
        var guards = 0;
        var scope = await h.Run(_ =>
        {
            guards++;
            return Task.CompletedTask;
        });
        Assert.True(scope.UsesDatabaseName);
        Assert.True(h.Fake.Now >= h.Fake.ReadyAt.AddSeconds(1));
        Assert.Equal(1, guards);
        Assert.Contains(h.Progress, m => m.StartsWith("Waiting for Facebook"));
        Assert.Contains(h.Progress, m => m.StartsWith("Previous upload cleared"));
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
    }

    [Fact]
    public async Task CapturedLd61FinishingMenuWaitsInsteadOfRejectingAsForeign()
    {
        var h = new Harness { TargetName = "Survival Outdoors" };
        h.Fake.Loading = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PFlowScreenTime",
                "ld61-next-part-finishing-menu.xml"
            )
        );
        Assert.True(PFlowConfirmation.HasPendingPublication(h.Fake.Loading));
        Assert.False(PFlowPostTargetSession.CanContinueFrom(h.Fake.Loading, "com.facebook.katana"));
        h.Fake.ReadyFrame = Header(h.TargetName);
        h.Fake.ReadyAt = h.Fake.Now.AddSeconds(3);
        Assert.True((await h.Run(_ => Task.CompletedTask)).UsesDatabaseName);
        Assert.Contains(h.Progress, m => m.StartsWith("Previous upload cleared"));
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
    }

    [Fact]
    public async Task ClearUiStillWaitsForUploadNotificationToBecomeInactive()
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        h.Fake.ReadyAt = h.Fake.Now.AddSeconds(2);
        h.Fake.Notifications = PFlowConfirmationTests.Dump(
            title: "Uploading your reel",
            flags: "2"
        );
        await h.Run(onDelay: () =>
        {
            if (h.Fake.Now >= DateTimeOffset.UnixEpoch.AddSeconds(5))
                h.Fake.Notifications = "Notification List:\n mArchive=Archive (0 notifications)";
        });
        Assert.True(h.Fake.Now >= DateTimeOffset.UnixEpoch.AddSeconds(6));
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    [Theory]
    [InlineData("ui")]
    [InlineData("notification")]
    [InlineData("unknown-notifications")]
    [InlineData("blank")]
    public async Task UnsettledPreviousUploadStopsSpecificallyWithoutRestart(string mode)
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        if (mode != "ui")
            h.Fake.ReadyAt = h.Fake.Now.AddSeconds(2);
        if (mode == "notification")
            h.Fake.Notifications = PFlowConfirmationTests.Dump(
                title: "Uploading your reel",
                flags: "2"
            );
        if (mode == "unknown-notifications")
            h.Fake.Notifications = "unavailable";
        if (mode == "blank")
            h.Fake.ReadyFrame = White;
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            h.Run(_ => Task.CompletedTask)
        );
        if (mode == "blank")
        {
            Assert.Contains("header", ex.Message);
            Assert.Equal(1, h.Fake.Stops);
            Assert.Equal(1, h.Fake.Launches);
            Assert.Equal(0, h.Fake.ForbiddenReads);
            return;
        }
        Assert.Contains("still finishing the previous upload", ex.Message);
        Assert.InRange((h.Fake.Now - DateTimeOffset.UnixEpoch).TotalSeconds, 30, 31);
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
    }

    [Fact]
    public async Task CancelDuringPreviousUploadWaitStopsWithoutActions()
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        using var stop = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            h.Run(ct: stop.Token, onDelay: () => stop.Cancel())
        );
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches + h.Fake.ForbiddenReads);
    }

    [Fact]
    public async Task ProcessChangeDuringPreviousUploadWaitCannotAuthorizeWork()
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Run(onDelay: () => h.Fake.Process = "999")
        );
        Assert.Contains("process changed", ex.Message);
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("input")]
    public async Task UnsafeFrameDuringPreviousUploadWaitCannotBeIgnored(string mode)
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Run(onDelay: () =>
            {
                if (mode == "foreign")
                    h.Fake.Activity = "other.app/Main";
                else
                    h.Fake.Loading = PendingHeader()
                        .Replace(
                            "</hierarchy>",
                            "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
                        );
            })
        );
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    [Fact]
    public async Task PendingHistoryAfterUploadClearStillBlocksNewActivation()
    {
        var h = new Harness();
        h.Fake.Loading = PendingHeader();
        h.Fake.ReadyAt = h.Fake.Now.AddSeconds(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            h.Run(_ => throw new InvalidOperationException("Uncertain publication"))
        );
        Assert.DoesNotContain(h.Progress, m => m.StartsWith("Previous upload cleared"));
        Assert.Equal(0, h.Fake.Stops + h.Fake.Launches);
    }

    public class Device : DispatchProxy
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch,
            ReadyAt = DateTimeOffset.MaxValue;
        public string Loading = White,
            Activity = "com.facebook.katana/Main",
            Notifications = "  Notification List:\n  mArchive=Archive (0 notifications)";
        public bool FirstHeader,
            Recover = true,
            StopOk = true;
        public string Process = "123",
            ReadyFrame = Header();
        public int Reads,
            Stops,
            Launches,
            ForbiddenReads;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(
                        FirstHeader && Reads++ == 0 || Now >= ReadyAt || Launches > 0 && Recover
                            ? ReadyFrame
                            : Loading
                    );
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(Activity);
                case "ExecuteAdbAsync":
                    if ((string)args![1]! == "shell pidof com.facebook.katana")
                        return Task.FromResult(Launches > 0 ? "456" : Process);
                    if ((string)args![1]! == "shell dumpsys notification --noredact")
                        return Task.FromResult(Notifications);
                    ForbiddenReads++;
                    throw new InvalidOperationException(
                        "No Graph, authentication, UID or clear-data command allowed"
                    );
                case "StopFacebookAppAsync":
                    Stops++;
                    return Task.FromResult(StopOk);
                case "LaunchAppAsync":
                    Launches++;
                    return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
