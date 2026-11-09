using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSubmissionReadScopeTests
{
    private sealed class Harness : IDisposable
    {
        internal readonly string Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "submission-reader-" + Guid.NewGuid().ToString("N")
        );
        internal readonly ILDPlayerService Ld = DispatchProxy.Create<ILDPlayerService, Device>();
        internal Device D => (Device)(object)Ld;
        internal PFlowJob Job;

        internal Harness()
        {
            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "vms/config"));
            Directory.CreateDirectory(System.IO.Path.Combine(Path, "vms/leidian900799"));
            File.WriteAllText(
                System.IO.Path.Combine(Path, "vms/config/leidian900799.config"),
                "{\"propertySettings.phoneAndroidId\":\"fixture\"}"
            );
            File.WriteAllText(
                System.IO.Path.Combine(Path, "vms/leidian900799/data.vmdk"),
                "fixture disk"
            );
            var binding = new PFlowAccountBinding(
                PFlowDeviceRouting.ReadIdentity(Path, 900799),
                900799,
                "Fixture LD",
                "com.facebook.katana",
                "100000000000001"
            );
            var target = new FacebookPageTarget
            {
                AccountBinding = binding,
                PageId = "100000000000002",
                UserUid = binding.OwnerUid,
                PageName = "Fixture Page",
                Type = "Page",
            };
            Job = new PFlowJob
            {
                AccountBinding = binding,
                DeviceIndex = binding.DeviceIndex,
                TargetId = target.PageId,
                OwnerUid = target.UserUid,
                TargetName = target.PageName,
                State = PFlowState.Submitted,
                SubmittedUtc = DateTime.UtcNow,
                Caption = "Fixture caption",
                NativeIdentityScope = PFlowNativeIdentityScope.FromSelectedName(
                    binding,
                    "123",
                    target,
                    new(target.PageName, target.Type),
                    [target]
                ),
            };
        }

        public void Dispose()
        {
            File.Delete(System.IO.Path.Combine(Path, "vms/config/leidian900799.config"));
            File.Delete(System.IO.Path.Combine(Path, "vms/leidian900799/data.vmdk"));
            Directory.Delete(System.IO.Path.Combine(Path, "vms/config"));
            Directory.Delete(System.IO.Path.Combine(Path, "vms/leidian900799"));
            Directory.Delete(System.IO.Path.Combine(Path, "vms"));
            Directory.Delete(Path);
        }
    }

    private static Task NoDelay(TimeSpan _, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static string Feed => FacebookLayoutRegistryTests.Fixture("feed-home");

    [Fact]
    public async Task ScopeCannotBeRestoredFromHistoryOrLeakAfterDisposal()
    {
        using var h = new Harness();
        var identity = h.Job.NativeIdentityScope;
        h.Job.NativeIdentityScope = null;
        Assert.Null(PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path));
        h.Job.NativeIdentityScope = identity;
        var scope = PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path, delay: NoDelay)!;
        scope.Dispose();
        var calls = 0;
        await Assert.ThrowsAsync<PFlowUiReaderUnavailableException>(() =>
            PFlowSubmissionReadScope.ReadAsync(
                h.Ld,
                h.Job.DeviceIndex,
                h.Path,
                _ =>
                {
                    calls++;
                    throw new PFlowUiReaderUnavailableException("Unscoped read");
                },
                default
            )
        );
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RecoveredVisibleFailureReturnsToConfirmationWithoutAcceptingIt()
    {
        using var h = new Harness();
        using var scope = PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path, delay: NoDelay);
        var calls = 0;
        var failureFrame = Feed.Replace(
            "</hierarchy>",
            "<node package='com.facebook.katana' text='Upload failed. Tap to retry'/></hierarchy>"
        );
        var xml = await PFlowSubmissionReadScope.ReadAsync(
            h.Ld,
            h.Job.DeviceIndex,
            h.Path,
            _ =>
                ++calls == 1
                    ? throw new PFlowUiReaderUnavailableException("Outage")
                    : Task.FromResult<string?>(failureFrame),
            default
        );
        Assert.Equal(2, calls);
        Assert.NotNull(PFlowPublicationFailure.Read(xml, h.Job.Caption));
        Assert.Null(h.Job.ConfirmedUtc);
        Assert.Equal(0, h.D.Inputs);
    }

    [Fact]
    public async Task TransientFailureRequiresTwoGuardedFreshReadsWithoutRenewingActorProof()
    {
        using var h = new Harness();
        var scope = h.Job.NativeIdentityScope!;
        using var monitor = PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path, delay: NoDelay);
        Assert.NotNull(monitor);
        var reads = 0;
        var result = await PFlowSubmissionReadScope.ReadAsync(
            h.Ld,
            h.Job.DeviceIndex,
            h.Path,
            _ =>
                ++reads == 1
                    ? throw new PFlowUiReaderUnavailableException("Fixture outage")
                    : Task.FromResult<string?>(Feed),
            default
        );
        Assert.Equal(Feed, result);
        Assert.Equal(3, reads);
        Assert.False(scope.Matches(h.Job));
        Assert.Equal(PFlowState.Submitted, h.Job.State);
        Assert.False(h.Job.CanRetry);
        Assert.Equal(0, h.D.Inputs);
    }

    [Fact]
    public async Task PersistentReaderFailureIsBoundedAndCannotConfirmOrRetrySubmission()
    {
        using var h = new Harness();
        using var monitor = PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path, delay: NoDelay);
        var reads = 0;
        await Assert.ThrowsAsync<PFlowUiReaderUnavailableException>(() =>
            PFlowSubmissionReadScope.ReadAsync(
                h.Ld,
                h.Job.DeviceIndex,
                h.Path,
                _ =>
                {
                    reads++;
                    throw new PFlowUiReaderUnavailableException("Fixture outage");
                },
                default
            )
        );
        Assert.Equal(4, reads); // original attempt plus three guarded reads
        Assert.Null(h.Job.ConfirmedUtc);
        Assert.Equal(0, h.D.Inputs);
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("foreground")]
    [InlineData("target")]
    [InlineData("caption")]
    [InlineData("submission-time")]
    [InlineData("binding")]
    public async Task ChangedSubmissionOrSessionStopsBeforeAnotherRead(string mode)
    {
        using var h = new Harness();
        using var monitor = PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path, delay: NoDelay);
        var reads = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowSubmissionReadScope.ReadAsync(
                h.Ld,
                h.Job.DeviceIndex,
                h.Path,
                _ =>
                {
                    reads++;
                    switch (mode)
                    {
                        case "pid":
                            h.D.Pid = "456";
                            break;
                        case "foreground":
                            h.D.Foreground = "other.app/Main";
                            break;
                        case "target":
                            h.Job.TargetId = "999";
                            break;
                        case "caption":
                            h.Job.Caption = "Changed caption";
                            break;
                        case "submission-time":
                            h.Job.SubmittedUtc = DateTime.UtcNow.AddDays(1);
                            break;
                        default:
                            h.Job.AccountBinding = h.Job.AccountBinding! with { OwnerUid = "999" };
                            break;
                    }
                    throw new PFlowUiReaderUnavailableException("Fixture outage");
                },
                default
            )
        );
        Assert.Equal(1, reads);
        Assert.Equal(0, h.D.Inputs);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("input")]
    public async Task UnsafeRecoveredFramesDoNotBecomeCompletionEvidence(string mode)
    {
        using var h = new Harness();
        using var monitor = PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path, delay: NoDelay);
        var reads = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowSubmissionReadScope.ReadAsync(
                h.Ld,
                h.Job.DeviceIndex,
                h.Path,
                _ =>
                {
                    if (++reads == 1)
                        throw new PFlowUiReaderUnavailableException("Fixture outage");
                    return Task.FromResult<string?>(
                        mode == "foreign"
                            ? Feed.Replace("com.facebook.katana", "other.app")
                            : Feed.Replace(
                                "</hierarchy>",
                                "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
                            )
                    );
                },
                default
            )
        );
        Assert.Equal(2, reads);
        Assert.Equal(0, h.D.Inputs);
    }

    [Fact]
    public async Task CancellationUnrelatedErrorsAndDifferentDevicesAreNeverRetried()
    {
        using var h = new Harness();
        using var monitor = PFlowSubmissionReadScope.Begin(h.Ld, h.Job, h.Path, delay: NoDelay);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowSubmissionReadScope.ReadAsync(
                h.Ld,
                h.Job.DeviceIndex,
                h.Path,
                _ => throw new InvalidOperationException("Authentication failure"),
                default
            )
        );
        await Assert.ThrowsAsync<PFlowUiReaderUnavailableException>(() =>
            PFlowSubmissionReadScope.ReadAsync(
                h.Ld,
                h.Job.DeviceIndex + 1,
                h.Path,
                _ => throw new PFlowUiReaderUnavailableException("Other device"),
                default
            )
        );
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowSubmissionReadScope.ReadAsync(
                h.Ld,
                h.Job.DeviceIndex,
                h.Path,
                _ => Task.FromCanceled<string?>(cancel.Token),
                cancel.Token
            )
        );
        Assert.Equal(0, h.D.Inputs);
    }

    public class Device : DispatchProxy
    {
        public string Pid = "123",
            Foreground = "com.facebook.katana/Main";
        public int Inputs;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "ExecuteAdbAsync":
                    Assert.Equal("shell pidof com.facebook.katana", args![1]);
                    return Task.FromResult(Pid);
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(Foreground);
                case "GetInstancesAsync":
                    return Task.FromResult<IReadOnlyList<LDInstanceInfo>>([
                        new()
                        {
                            Index = 900799,
                            Name = "Fixture LD",
                            IsAndroidStarted = true,
                        },
                    ]);
                default:
                    Inputs++;
                    throw new InvalidOperationException("Unexpected device action: " + method.Name);
            }
        }
    }
}
