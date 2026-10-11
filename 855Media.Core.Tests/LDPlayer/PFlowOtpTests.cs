using System.Text;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowOtpTests
{
    private const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // RFC fixture, never a real account.
    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        900074,
        "fixture-LD",
        "com.facebook.katana",
        "123"
    );

    private static PFlowCredential Credential(bool totp = false) =>
        PFlowCredentialImport.Parse(
            "123|fixture-password|" + (totp ? Secret : "") + "|",
            Binding,
            "fixture@example.invalid",
            totp ? PFlowTwoFactorKind.TotpSecret : PFlowTwoFactorKind.None
        );

    private static PFlowOtpRequest Request(
        TimeProvider clock,
        PFlowOtpMethod method = PFlowOtpMethod.Sms
    ) => new(Binding, "fixture-challenge", method, 6, clock.GetUtcNow());

    [Fact]
    public void AuthenticatorExpiryAndMinimumRemainingTime()
    {
        using var code = PFlowOtpCode.Authenticator(Secret, DateTimeOffset.FromUnixTimeSeconds(52));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(60), code.ExpiresUtc);
        Assert.True(code.IsUsable(DateTimeOffset.FromUnixTimeSeconds(52), 6));
        Assert.False(code.IsUsable(DateTimeOffset.FromUnixTimeSeconds(53), 6));
        Assert.False(code.IsUsable(DateTimeOffset.FromUnixTimeSeconds(52), 8));
        using var eight = PFlowOtpCode.Authenticator(
            Secret,
            DateTimeOffset.FromUnixTimeSeconds(59),
            8
        );
        Assert.Equal("94287082", Encoding.ASCII.GetString(eight.Value));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("１２３４５６")]
    [InlineData("123 56")]
    [InlineData("123456\n")]
    public void RejectsReceivedInvalidCodesWithoutEcho(string value)
    {
        var ex = Assert.Throws<FormatException>(() =>
            PFlowOtpCode.Received(value, DateTimeOffset.UtcNow)
        );
        Assert.DoesNotContain(value, ex.Message);
    }

    [Fact]
    public void ReceivedLeadingZerosAndEightDigitsSurviveAndSecretsCannotSerialize()
    {
        var now = DateTimeOffset.UtcNow;
        var code = PFlowOtpCode.Received("001234", now);
        Assert.Equal("001234", Encoding.ASCII.GetString(code.Value));
        Assert.Equal(now.AddSeconds(60), code.ExpiresUtc);
        Assert.Equal("{}", JsonSerializer.Serialize(code));
        Assert.DoesNotContain("001234", code.ToString());
        code.Dispose();
        Assert.All(code.Value, b => Assert.Equal(0, b));
        Assert.False(code.IsUsable(now, 6));
        using var eight = PFlowOtpCode.Received("00123456", now, 8);
        Assert.Equal(8, eight.Value.Length);
    }

    [Theory]
    [InlineData(PFlowOtpMethod.Authenticator, false)]
    [InlineData(PFlowOtpMethod.Sms, true)]
    [InlineData(PFlowOtpMethod.Email, true)]
    public async Task CodeSourceChoosesMethodWithoutUsingTotpForMessages(
        PFlowOtpMethod method,
        bool manual
    )
    {
        var clock = new Clock(30);
        using var credential = Credential(true);
        int calls = 0;
        var source = new PFlowOtpCodeSource(
            (r, _) =>
            {
                calls++;
                return Task.FromResult(
                    PFlowOtpCode.Received("000123", clock.GetUtcNow(), r.Digits)
                );
            },
            clock
        );
        using var code = await source.ReceiveAsync(Request(clock, method), credential, default);
        Assert.Equal(manual ? 1 : 0, calls);
        Assert.Equal(
            manual ? "000123" : PFlowTotp.Generate(Secret, clock.GetUtcNow()),
            Encoding.ASCII.GetString(code.Value)
        );
    }

    [Theory]
    [InlineData(59, 60)]
    [InlineData(30, 60)]
    public async Task SourceWaitsForNextWindowRatherThanSubmittingNearExpiryOrRejectedCode(
        long now,
        long expected
    )
    {
        var clock = new Clock(now);
        using var credential = Credential(true);
        var request = Request(clock, PFlowOtpMethod.Authenticator);
        if (now == 30)
            request = request with { NotBeforeUtc = DateTimeOffset.FromUnixTimeSeconds(60) };
        using var code = await new PFlowOtpCodeSource(
            (_, _) => throw new Exception("Not manual"),
            clock
        ).ReceiveAsync(request, credential, default);
        Assert.Equal(expected, clock.GetUtcNow().ToUnixTimeSeconds());
        Assert.True(code.IsUsable(clock.GetUtcNow(), 6));
        Assert.Equal(
            PFlowTotp.Generate(Secret, clock.GetUtcNow()),
            Encoding.ASCII.GetString(code.Value)
        );
    }

    [Fact]
    public async Task SourceStopsForDisposedCredentialAndCancellation()
    {
        var clock = new Clock(59);
        var source = new PFlowOtpCodeSource((_, _) => throw new Exception("Not manual"), clock);
        var credential = Credential(true);
        credential.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            source.ReceiveAsync(Request(clock), credential, default)
        );
        using var live = Credential(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            source.ReceiveAsync(Request(clock), live, cancellation.Token)
        );
    }

    [Fact]
    public async Task InboxRequiresExactBindingAndChallengeAndAcceptsOneValidCode()
    {
        var clock = new Clock(30);
        var request = Request(clock);
        PFlowOtpRequest? shown = null;
        using var inbox = new PFlowOtpInbox(Binding, r => shown = r, clock);
        var pending = inbox.ReceiveAsync(request, default);
        Assert.Equal(request, shown);
        Assert.False(
            inbox.TrySubmit(Binding with { DeviceIndex = 2 }, request.ChallengeId, "123456", out _)
        );
        Assert.False(inbox.TrySubmit(Binding, "other", "123456", out _));
        Assert.False(inbox.TrySubmit(Binding, request.ChallengeId, "bad", out var error));
        Assert.DoesNotContain("bad", error);
        Assert.False(pending.IsCompleted);
        Assert.True(inbox.TrySubmit(Binding, request.ChallengeId, "001234", out _));
        Assert.False(inbox.TrySubmit(Binding, request.ChallengeId, "123456", out _));
        using var result = await pending;
        Assert.Equal("001234", Encoding.ASCII.GetString(result.Value));
        Assert.Null(shown);
    }

    [Fact]
    public async Task InboxCancellationDisposalAndCleanupFailureDoNotLeaveAReceiver()
    {
        var clock = new Clock(30);
        using var cancellation = new CancellationTokenSource();
        using var inbox = new PFlowOtpInbox(
            Binding,
            r =>
            {
                if (r == null)
                    throw new Exception("Cleanup fixture");
            },
            clock
        );
        var task = inbox.ReceiveAsync(Request(clock), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(inbox.TrySubmit(Binding, "fixture-challenge", "123456", out _));
        var second = inbox.ReceiveAsync(Request(clock), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inbox.ReceiveAsync(Request(clock), default)
        );
        inbox.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(inbox.TrySubmit(Binding, "fixture-challenge", "123456", out _));
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("foreign")]
    [InlineData("invalid-digits")]
    public async Task UnsupportedOrStaleRequestNeverReceivesACode(string reason)
    {
        var clock = new Clock(30);
        var request = Request(clock);
        request = reason switch
        {
            "stale" => request with { ObservedUtc = clock.GetUtcNow().AddSeconds(-11) },
            "future" => request with { ObservedUtc = clock.GetUtcNow().AddSeconds(1) },
            "foreign" => request with { Binding = Binding with { OwnerUid = "456" } },
            _ => request with { Digits = 7 },
        };
        using var inbox = new PFlowOtpInbox(
            Binding,
            _ => throw new Exception("No UI request"),
            clock
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inbox.ReceiveAsync(request, default)
        );
    }

    [Theory]
    [InlineData(PFlowOtpSubmission.Confirmed, 1)]
    [InlineData(PFlowOtpSubmission.Rejected, 3)]
    [InlineData(PFlowOtpSubmission.Unknown, 1)]
    public async Task SubmissionIsBoundedAndUnknownNeverRetries(
        PFlowOtpSubmission outcome,
        int calls
    )
    {
        var clock = new Clock(30);
        var adapter = new Adapter(clock) { Outcome = outcome };
        var source = new Source(clock);
        var progress = new List<string>();
        using var credential = Credential();
        var task = new PFlowOtpAuthenticationService(adapter, source, clock).AuthenticateAsync(
            Binding,
            credential,
            s =>
            {
                progress.Add(s);
                return Task.CompletedTask;
            },
            default
        );
        if (outcome == PFlowOtpSubmission.Confirmed)
            await task;
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal(calls, adapter.Submits);
        Assert.All(source.Codes, c => Assert.True(c.IsDisposed));
        Assert.All(
            progress,
            p =>
            {
                Assert.DoesNotContain("fixture-password", p);
                Assert.DoesNotContain("123450", p);
            }
        );
    }

    [Fact]
    public async Task ReusedReceivedCodeCannotBeSubmittedTwice()
    {
        var clock = new Clock(30);
        var adapter = new Adapter(clock) { Outcome = PFlowOtpSubmission.Rejected };
        using var credential = Credential();
        var source = new Source(clock) { Repeat = true };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowOtpAuthenticationService(adapter, source, clock).AuthenticateAsync(
                Binding,
                credential,
                _ => Task.CompletedTask,
                default
            )
        );
        Assert.Contains("already submitted", ex.Message);
        Assert.Equal(1, adapter.Submits);
        Assert.All(source.Codes, c => Assert.True(c.IsDisposed));
    }

    [Theory]
    [InlineData("challenge")]
    [InlineData("worker")]
    [InlineData("expired")]
    public async Task RechecksCurrentChallengeWorkerAndExpiryAfterWaiting(string reason)
    {
        var clock = new Clock(30);
        var adapter = new Adapter(clock);
        using var credential = Credential();
        var source = new Source(clock)
        {
            AfterReceive = () =>
            {
                if (reason == "challenge")
                    adapter.ChangedChallenge = true;
                if (reason == "worker")
                    adapter.ChangedWorker = true;
                if (reason == "expired")
                    clock.Advance(TimeSpan.FromSeconds(60));
            },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowOtpAuthenticationService(adapter, source, clock).AuthenticateAsync(
                Binding,
                credential,
                _ => Task.CompletedTask,
                default
            )
        );
        Assert.Equal(0, adapter.Submits);
        Assert.All(source.Codes, c => Assert.True(c.IsDisposed));
    }

    [Theory]
    [InlineData(PFlowOtpChallengeState.NotRequired, false)]
    [InlineData(PFlowOtpChallengeState.Checkpoint, true)]
    [InlineData(PFlowOtpChallengeState.Unsupported, true)]
    public async Task DoesNotInventCodeChallengesOrBypassCheckpoints(
        PFlowOtpChallengeState state,
        bool error
    )
    {
        var clock = new Clock(30);
        using var credential = Credential();
        var adapter = new Adapter(clock) { State = state };
        var source = new Source(clock);
        var task = new PFlowOtpAuthenticationService(adapter, source, clock).AuthenticateAsync(
            Binding,
            credential,
            _ => Task.CompletedTask,
            default
        );
        if (error)
            await Assert.ThrowsAsync<NotSupportedException>(() => task);
        else
            await task;
        Assert.Empty(source.Codes);
        Assert.Equal(0, adapter.Submits);
    }

    [Fact]
    public async Task CancelWhileWaitingForReceivedCodeClearsInboxAndNeverSubmits()
    {
        var clock = new Clock(30);
        PFlowOtpRequest? shown = null;
        using var inbox = new PFlowOtpInbox(Binding, r => shown = r, clock);
        using var credential = Credential();
        using var cancellation = new CancellationTokenSource();
        var adapter = new Adapter(clock);
        var task = new PFlowOtpAuthenticationService(
            adapter,
            new PFlowOtpCodeSource(inbox.ReceiveAsync, clock),
            clock
        ).AuthenticateAsync(Binding, credential, _ => Task.CompletedTask, cancellation.Token);
        Assert.NotNull(shown);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Null(shown);
        Assert.Equal(0, adapter.Submits);
    }

    [Fact]
    public async Task ExpiryDuringProgressCannotCauseALateSubmission()
    {
        var clock = new Clock(30);
        var adapter = new Adapter(clock);
        var source = new Source(clock);
        using var credential = Credential();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowOtpAuthenticationService(adapter, source, clock).AuthenticateAsync(
                Binding,
                credential,
                s =>
                {
                    if (s.StartsWith("Submitting", StringComparison.Ordinal))
                        clock.Advance(TimeSpan.FromSeconds(60));
                    return Task.CompletedTask;
                },
                default
            )
        );
        Assert.Equal(0, adapter.Submits);
        Assert.All(source.Codes, c => Assert.True(c.IsDisposed));
    }

    [Fact]
    public async Task RejectedAuthenticatorCodeWaitsForAnotherWindow()
    {
        var clock = new Clock(30);
        var adapter = new Adapter(clock)
        {
            Method = PFlowOtpMethod.Authenticator,
            RejectFirst = true,
        };
        using var credential = Credential(true);
        var source = new PFlowOtpCodeSource(
            (_, _) => throw new Exception("No manual code needed"),
            clock
        );
        await new PFlowOtpAuthenticationService(adapter, source, clock).AuthenticateAsync(
            Binding,
            credential,
            _ => Task.CompletedTask,
            default
        );
        Assert.Equal(new long[] { 30, 60 }, adapter.SubmissionTimes);
        Assert.Equal(2, adapter.Submits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedCodeStillRequiresExactLiveUidBeforeCredentialSave(bool wrongUid)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "pflow-otp-add-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            using var input = PFlowAddAccountInput.Parse(
                "123|fixture-password||10/3/2026|fixture@example.com",
                Binding.DeviceIndex,
                Binding.DeviceName,
                Binding.InstanceIdentity,
                Binding.AppPackage
            );
            var store = new PFlowAccountStore(Path.Combine(directory, "accounts.json"));
            var credentials = new CredentialStore();
            var navigator = new LoginNavigator(wrongUid);
            var task = new PFlowAddAccountService(store, credentials, navigator).AddAsync(
                input,
                _ => Task.CompletedTask,
                default
            );
            if (wrongUid)
                await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            else
                Assert.True((await task).InstanceLocked);
            Assert.Equal(1, navigator.OtpSubmissions);
            Assert.Equal(wrongUid ? 0 : 1, credentials.Saves);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private sealed class LoginNavigator(bool wrongUid) : IPFlowLoginNavigator
    {
        public int OtpSubmissions { get; private set; }

        public Task<PFlowWorkerIdentity> ReadWorkerAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new PFlowWorkerIdentity(
                    b.DeviceIndex,
                    b.DeviceName,
                    b.InstanceIdentity,
                    b.AppPackage
                )
            );

        public async Task<PFlowAccountObservation> LoginAsync(
            PFlowAccountBinding b,
            PFlowCredential c,
            Func<string, Task> progress,
            CancellationToken ct
        )
        {
            var clock = new Clock(30);
            var adapter = new Adapter(clock);
            await new PFlowOtpAuthenticationService(
                adapter,
                new Source(clock),
                clock
            ).AuthenticateAsync(b, c, progress, ct);
            OtpSubmissions = adapter.Submits;
            return new(
                wrongUid ? "456" : b.OwnerUid,
                b.AppPackage,
                true,
                true,
                true,
                DateTimeOffset.UtcNow,
                PFlowAuthenticationState.Authenticated
            );
        }
    }

    private sealed class CredentialStore : IPFlowCredentialStore
    {
        public int Saves { get; private set; }

        public Task<string> SaveAsync(
            PFlowAccountBinding b,
            PFlowCredential c,
            CancellationToken ct = default
        )
        {
            Saves++;
            return Task.FromResult("fixture-reference");
        }

        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding b,
            string reference,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public void Delete(string reference) { }
    }

    private sealed class Source(Clock clock) : IPFlowOtpCodeSource
    {
        public List<PFlowOtpCode> Codes { get; } = [];
        public bool Repeat { get; init; }
        public Action? AfterReceive { get; init; }

        public Task<PFlowOtpCode> ReceiveAsync(
            PFlowOtpRequest r,
            PFlowCredential c,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            var code = PFlowOtpCode.Received(
                "12345" + (Repeat ? 0 : Codes.Count),
                clock.GetUtcNow()
            );
            Codes.Add(code);
            AfterReceive?.Invoke();
            return Task.FromResult(code);
        }
    }

    private sealed class Adapter(Clock clock) : IPFlowOtpChallengeAdapter
    {
        public bool ChangedChallenge { get; set; }
        public bool ChangedWorker { get; set; }
        public PFlowOtpSubmission Outcome { get; init; } = PFlowOtpSubmission.Confirmed;
        public PFlowOtpMethod Method { get; init; } = PFlowOtpMethod.Sms;
        public bool RejectFirst { get; init; }
        public List<long> SubmissionTimes { get; } = [];
        public PFlowOtpChallengeState State { get; init; } = PFlowOtpChallengeState.CodeRequired;
        public int Submits { get; private set; }

        public Task<PFlowWorkerIdentity> ReadWorkerAsync(
            PFlowAccountBinding b,
            CancellationToken ct
        ) =>
            Task.FromResult(
                new PFlowWorkerIdentity(
                    ChangedWorker ? 2 : b.DeviceIndex,
                    b.DeviceName,
                    b.InstanceIdentity,
                    b.AppPackage
                )
            );

        public Task<PFlowOtpChallenge> ObserveAsync(PFlowAccountBinding b, CancellationToken ct) =>
            Task.FromResult(
                new PFlowOtpChallenge(
                    State,
                    Request(clock, Method) with
                    {
                        ChallengeId = ChangedChallenge ? "other" : "fixture-challenge",
                    }
                )
            );

        public Task<PFlowOtpSubmission> SubmitAsync(
            PFlowOtpRequest r,
            PFlowOtpCode code,
            CancellationToken ct
        )
        {
            Assert.Equal(Binding, r.Binding);
            Assert.True(code.IsUsable(clock.GetUtcNow(), r.Digits));
            Submits++;
            SubmissionTimes.Add(clock.GetUtcNow().ToUnixTimeSeconds());
            return Task.FromResult(
                RejectFirst && Submits == 1 ? PFlowOtpSubmission.Rejected : Outcome
            );
        }
    }

    private sealed class Clock(long seconds) : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(seconds);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan time) => _now += time;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            Advance(dueTime);
            return new FixtureTimer(callback, state);
        }

        private sealed class FixtureTimer : ITimer
        {
            private readonly Timer _timer;

            public FixtureTimer(TimerCallback callback, object? state) =>
                _timer = new Timer(callback, state, 1, Timeout.Infinite);

            public bool Change(TimeSpan dueTime, TimeSpan period) => _timer.Change(dueTime, period);

            public void Dispose() => _timer.Dispose();

            public ValueTask DisposeAsync() => _timer.DisposeAsync();
        }
    }
}
