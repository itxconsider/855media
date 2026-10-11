using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public enum PFlowOtpMethod
{
    Authenticator,
    Sms,
    Email,
}

public enum PFlowOtpChallengeState
{
    CodeRequired,
    NotRequired,
    Unsupported,
    Checkpoint,
}

public enum PFlowOtpSubmission
{
    Confirmed,
    Rejected,
    Unknown,
}

// An adapter must derive this from the current installed screen, not cached account data.
public sealed record PFlowOtpRequest(
    PFlowAccountBinding Binding,
    string ChallengeId,
    PFlowOtpMethod Method,
    int Digits,
    DateTimeOffset ObservedUtc,
    DateTimeOffset? NotBeforeUtc = null
)
{
    public void RequireFresh(PFlowAccountBinding binding, DateTimeOffset now)
    {
        binding.Validate();
        if (
            Binding != binding
            || string.IsNullOrWhiteSpace(ChallengeId)
            || !Enum.IsDefined(Method)
            || Digits is not (6 or 8)
            || ObservedUtc > now
            || ObservedUtc < now.AddSeconds(-10)
        )
            throw new InvalidOperationException(
                "The verification challenge is stale or belongs to another account, LD or app."
            );
    }
}

public sealed record PFlowOtpChallenge(
    PFlowOtpChallengeState State,
    PFlowOtpRequest? Request = null
);

public sealed class PFlowOtpCode : IDisposable
{
    [JsonIgnore]
    public byte[] Value { get; }

    [JsonIgnore]
    public DateTimeOffset ExpiresUtc { get; }

    [JsonIgnore]
    public bool IsDisposed { get; private set; }

    private PFlowOtpCode(string value, DateTimeOffset expires) =>
        (Value, ExpiresUtc) = (Encoding.ASCII.GetBytes(value), expires);

    public static PFlowOtpCode Authenticator(string secret, DateTimeOffset now, int digits = 6) =>
        new(
            PFlowTotp.Generate(secret, now, digits),
            DateTimeOffset.FromUnixTimeSeconds((now.ToUnixTimeSeconds() / 30 + 1) * 30)
        );

    // This is a local freshness limit, not a claim about the SMS/email issuer's expiry.
    public static PFlowOtpCode Received(string value, DateTimeOffset now, int digits = 6)
    {
        if (digits is not (6 or 8) || value.Length != digits || !value.All(char.IsAsciiDigit))
            throw new FormatException(
                $"Enter exactly {digits} digits from the current verification message."
            );
        return new(value, now.AddSeconds(60));
    }

    public bool IsUsable(DateTimeOffset now, int digits) =>
        !IsDisposed
        && Value.Length == digits
        && Value.All(b => b >= '0' && b <= '9')
        && ExpiresUtc >= now.AddSeconds(8);

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Value);
        IsDisposed = true;
    }

    public override string ToString() => "Authentication code (hidden)";
}

public interface IPFlowOtpCodeSource
{
    Task<PFlowOtpCode> ReceiveAsync(
        PFlowOtpRequest request,
        PFlowCredential credential,
        CancellationToken ct
    );
}

// Authenticator codes are generated locally. SMS/email codes come only from explicit user input.
public sealed class PFlowOtpCodeSource(
    Func<PFlowOtpRequest, CancellationToken, Task<PFlowOtpCode>> receive,
    TimeProvider? clock = null
) : IPFlowOtpCodeSource
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PFlowOtpCode> ReceiveAsync(
        PFlowOtpRequest request,
        PFlowCredential credential,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(credential.IsDisposed, credential);
        if (
            request.Method != PFlowOtpMethod.Authenticator
            || credential.TwoFactorKind != PFlowTwoFactorKind.TotpSecret
        )
            return await receive(request, ct);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(credential.IsDisposed, credential);
            var now = _clock.GetUtcNow();
            if (request.NotBeforeUtc is { } next && next > now)
            {
                await Task.Delay(next - now, _clock, ct);
                continue;
            }
            var code = PFlowOtpCode.Authenticator(
                Encoding.UTF8.GetString(credential.TwoFactor),
                now,
                request.Digits
            );
            if (code.IsUsable(now, request.Digits))
                return code;
            var delay = code.ExpiresUtc - now;
            code.Dispose();
            await Task.Delay(delay, _clock, ct);
        }
    }
}

// One in-memory inbox per login. No shared OTP between different account bindings/challenges.
public sealed class PFlowOtpInbox(
    PFlowAccountBinding binding,
    Action<PFlowOtpRequest?> changed,
    TimeProvider? clock = null
) : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private TaskCompletionSource<PFlowOtpCode>? _pending;
    private PFlowOtpRequest? _request;
    private bool _disposed;

    public async Task<PFlowOtpCode> ReceiveAsync(PFlowOtpRequest request, CancellationToken ct)
    {
        request.RequireFresh(binding, _clock.GetUtcNow());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var pending = new TaskCompletionSource<PFlowOtpCode>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending != null)
                throw new InvalidOperationException(
                    "Another verification-code request is already waiting."
                );
            timeout.Token.ThrowIfCancellationRequested();
            _pending = pending;
            _request = request;
        }
        using var registration = timeout.Token.Register(() =>
            pending.TrySetCanceled(timeout.Token)
        );
        PFlowOtpCode? delivered = null;
        bool transferred = false;
        try
        {
            changed(request);
            delivered = await pending.Task;
            timeout.Token.ThrowIfCancellationRequested();
            lock (_gate)
                if (_disposed)
                    throw new OperationCanceledException("Code request closed.");
            transferred = true;
            return delivered;
        }
        finally
        {
            lock (_gate)
            {
                _pending = null;
                _request = null;
            }
            if (delivered == null && pending.Task.IsCompletedSuccessfully)
                delivered = pending.Task.Result;
            if (!transferred)
                delivered?.Dispose();
            // UI cleanup must never hide cancellation or a primary authentication failure.
            try
            {
                changed(null);
            }
            catch { }
        }
    }

    public bool TrySubmit(
        PFlowAccountBinding account,
        string challengeId,
        string value,
        out string error
    )
    {
        lock (_gate)
        {
            error = "No matching verification-code request is waiting.";
            if (
                _disposed
                || _request == null
                || _pending == null
                || _pending.Task.IsCompleted
                || account != binding
                || challengeId != _request.ChallengeId
            )
                return false;
            PFlowOtpCode code;
            try
            {
                code = PFlowOtpCode.Received(value, _clock.GetUtcNow(), _request.Digits);
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
            if (!_pending.TrySetResult(code))
            {
                code.Dispose();
                return false;
            }
            error = "Code received for this login; checking the current challenge.";
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending?.TrySetCanceled();
            _request = null;
        }
    }
}

public interface IPFlowOtpChallengeAdapter
{
    Task<PFlowWorkerIdentity> ReadWorkerAsync(PFlowAccountBinding binding, CancellationToken ct);
    Task<PFlowOtpChallenge> ObserveAsync(PFlowAccountBinding binding, CancellationToken ct);

    // Recheck the assigned worker, expected challenge/foreground and code expiry before typing. Confirm only its correlated outcome;
    // an unrecognized screen or missing outcome must return Unknown, never a guessed success.
    Task<PFlowOtpSubmission> SubmitAsync(
        PFlowOtpRequest expected,
        PFlowOtpCode code,
        CancellationToken ct
    );
}

public sealed class PFlowOtpAuthenticationService(
    IPFlowOtpChallengeAdapter adapter,
    IPFlowOtpCodeSource codes,
    TimeProvider? clock = null
)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task AuthenticateAsync(
        PFlowAccountBinding binding,
        PFlowCredential credential,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        binding.Validate();
        ObjectDisposedException.ThrowIf(credential.IsDisposed, credential);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var used = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset? nextTotp = null;
        async Task<PFlowOtpChallenge> Observe()
        {
            token.ThrowIfCancellationRequested();
            var worker = await adapter.ReadWorkerAsync(binding, token);
            binding.RequireWorker(
                worker.DeviceIndex,
                worker.DeviceName,
                worker.InstanceIdentity,
                worker.AppPackage
            );
            var observed = await adapter.ObserveAsync(binding, token);
            if (observed.State == PFlowOtpChallengeState.CodeRequired)
            {
                if (observed.Request == null)
                    throw new InvalidOperationException(
                        "Verification screen lacks a supported code request."
                    );
                observed.Request.RequireFresh(binding, _clock.GetUtcNow());
            }
            else if (observed.State != PFlowOtpChallengeState.NotRequired)
                throw new NotSupportedException(
                    "Facebook requires an unsupported challenge or checkpoint. Complete it inside the assigned LD."
                );
            return observed;
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var observed = await Observe();
            if (observed.State == PFlowOtpChallengeState.NotRequired)
                return;
            var request = observed.Request! with { NotBeforeUtc = nextTotp };
            await progress(
                request.Method == PFlowOtpMethod.Authenticator
                    ? "Preparing an authenticator code for this account."
                    : "Waiting for the code received by SMS or email. Enter it in Add Account."
            );
            using var code = await codes.ReceiveAsync(request, credential, token);
            var current = await Observe();
            if (current.State == PFlowOtpChallengeState.NotRequired)
                return;
            var live = current.Request!;
            if (
                live.ChallengeId != request.ChallengeId
                || live.Method != request.Method
                || live.Digits != request.Digits
            )
                throw new InvalidOperationException(
                    "The verification challenge changed. Request a new code for the current screen."
                );
            if (!code.IsUsable(_clock.GetUtcNow(), request.Digits))
                throw new InvalidOperationException(
                    "The code expired or has too little time remaining. Request a new code."
                );
            if (!used.Add(Convert.ToHexString(SHA256.HashData(code.Value))))
                throw new InvalidOperationException(
                    "This code was already submitted. Use a new code from the current challenge."
                );
            token.ThrowIfCancellationRequested();
            await progress("Submitting one code to the confirmed verification challenge.");
            token.ThrowIfCancellationRequested();
            if (!code.IsUsable(_clock.GetUtcNow(), request.Digits))
                throw new InvalidOperationException(
                    "The code expired before submission. Request a new code."
                );
            var outcome = await adapter.SubmitAsync(live, code, token);
            if (outcome == PFlowOtpSubmission.Confirmed)
            {
                await progress(
                    "Verification code accepted. Confirming the personal account UID next."
                );
                return;
            }
            if (outcome != PFlowOtpSubmission.Rejected)
                throw new InvalidOperationException(
                    "Code submission outcome was not confirmed. Review Facebook before retrying; no duplicate code was sent."
                );
            nextTotp = request.Method == PFlowOtpMethod.Authenticator ? code.ExpiresUtc : null;
            await progress(
                "Facebook rejected this code. Waiting for a new code; check the account secret and Windows clock."
            );
        }
        throw new InvalidOperationException(
            "Three verification codes were rejected. Review the challenge inside this LD before retrying."
        );
    }
}
