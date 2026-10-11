using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Create only for a single, explicitly submitted login attempt on this binding.
// Acceptance of the code never substitutes for the separate exact UID verifier.
public sealed class PFlowNativeOtpChallengeAdapter(
    ILDPlayerService ld,
    string path,
    PFlowAccountBinding attemptBinding,
    bool selectAuthenticator = false
) : IPFlowOtpChallengeAdapter
{
    private readonly string _challengeId = Guid.NewGuid().ToString("N");
    private readonly PFlowSessionPreparation _preparation = new(ld, path);
    private bool _submitted;
    private bool _alternateSelected;
    private bool _methodSelected;
    private string? _accountMarker;

    public void BindNetworkIdentity(string identity) => _preparation.BindNetworkIdentity(identity);

    public Task<PFlowWorkerIdentity> ReadWorkerAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        RequireAttempt(binding);
        return _preparation.RequireWorkerAsync(binding, ct);
    }

    private void RequireAttempt(PFlowAccountBinding binding)
    {
        if (binding != attemptBinding || binding.AppPackage != "com.facebook.katana")
            throw new InvalidOperationException(
                "Verification belongs to a different login attempt or app."
            );
    }

    private async Task<string> Screen(PFlowAccountBinding binding, CancellationToken ct)
    {
        await ReadWorkerAsync(binding, ct);
        await _preparation.RequireConnectionAsync(binding, ct);
        var focused = await ld.GetCurrentFocusedActivityAsync(binding.DeviceIndex, path, ct) ?? "";
        if (
            !System.Text.RegularExpressions.Regex.IsMatch(
                focused,
                @"(?<![\w.])com\.facebook\.katana/"
            )
        )
            throw new InvalidOperationException(
                "Facebook is not the foreground app. Verification stopped."
            );
        return await ld.DumpUiHierarchyAsync(binding.DeviceIndex, path, ct) ?? "";
    }

    private async Task Tap(
        PFlowAccountBinding binding,
        string role,
        PFlowLoginScreenKind expected,
        CancellationToken ct
    )
    {
        var xml = await Screen(binding, ct);
        if (PFlowLoginScreen.Classify(xml) != expected)
            throw new InvalidOperationException("Verification screen changed before navigation.");
        var point = PFlowNativeLoginNavigator.Control(xml, role);
        if (
            point == null
            || !await ld.TapAsync(binding.DeviceIndex, point.Value.X, point.Value.Y, path, ct)
        )
            throw new InvalidOperationException("Supported verification control is unavailable.");
    }

    public async Task<PFlowOtpChallenge> ObserveAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        // Bounded loading wait; there is no password or code retry in observation.
        var until = DateTimeOffset.UtcNow.AddSeconds(20);
        while (true)
        {
            var xml = await Screen(binding, ct);
            var kind = PFlowLoginScreen.Classify(xml);
            if (kind == PFlowLoginScreenKind.DeviceApprovalRequired && selectAuthenticator)
            {
                if (_alternateSelected)
                    return new(PFlowOtpChallengeState.Unsupported);
                _alternateSelected = true;
                await Tap(binding, "Try another way", kind, ct);
                await Task.Delay(500, ct);
            }
            else if (kind == PFlowLoginScreenKind.VerificationMethods && selectAuthenticator)
            {
                if (_methodSelected)
                    return new(PFlowOtpChallengeState.Unsupported);
                _methodSelected = true;
                await Tap(
                    binding,
                    "Authentication app, Get a code from your authentication app.",
                    kind,
                    ct
                );
                var selected = await Screen(binding, ct);
                if (
                    PFlowLoginScreen.Classify(selected) != kind
                    || !XDocument
                        .Parse(selected)
                        .Descendants("node")
                        .Any(n =>
                            (string?)n.Attribute("package") == binding.AppPackage
                            && (string?)n.Attribute("class") == "android.widget.RadioButton"
                            && (string?)n.Attribute("content-desc")
                                == "Authentication app, Get a code from your authentication app."
                            && (string?)n.Attribute("checked") == "true"
                        )
                )
                    throw new InvalidOperationException(
                        "Authenticator verification method was not selected."
                    );
                await Tap(binding, "Continue", kind, ct);
                await Task.Delay(500, ct);
            }
            else if (kind == PFlowLoginScreenKind.AuthenticatorCodeRequired)
            {
                RequireAccountMarker(xml);
                return new(
                    PFlowOtpChallengeState.CodeRequired,
                    new(
                        binding,
                        _challengeId,
                        PFlowOtpMethod.Authenticator,
                        6,
                        DateTimeOffset.UtcNow
                    )
                );
            }
            else if (kind == PFlowLoginScreenKind.SaveLoginInfo)
                return new(PFlowOtpChallengeState.NotRequired);
            else if (kind == PFlowLoginScreenKind.Checkpoint)
                return new(PFlowOtpChallengeState.Checkpoint);
            else if (
                kind
                is PFlowLoginScreenKind.CredentialsRejected
                    or PFlowLoginScreenKind.LoginRequired
                    or PFlowLoginScreenKind.DeviceApprovalRequired
                    or PFlowLoginScreenKind.VerificationMethods
            )
                throw new InvalidOperationException(PFlowLoginScreen.Message(kind));
            else
                await Task.Delay(500, ct);
            if (DateTimeOffset.UtcNow >= until)
                return new(PFlowOtpChallengeState.Unsupported);
        }
    }

    public async Task<PFlowOtpSubmission> SubmitAsync(
        PFlowOtpRequest request,
        PFlowOtpCode code,
        CancellationToken ct
    )
    {
        request.RequireFresh(attemptBinding, DateTimeOffset.UtcNow);
        if (
            _submitted
            || request.ChallengeId != _challengeId
            || request.Method != PFlowOtpMethod.Authenticator
            || request.Digits != 6
            || !code.IsUsable(DateTimeOffset.UtcNow, 6)
        )
            throw new InvalidOperationException(
                "Verification code or attempt is not ready for submission."
            );
        var xml = await Screen(request.Binding, ct);
        if (PFlowLoginScreen.Classify(xml) != PFlowLoginScreenKind.AuthenticatorCodeRequired)
            throw new InvalidOperationException("Authenticator screen changed before code entry.");
        if (_accountMarker == null)
            throw new InvalidOperationException(
                "Observe the bound verification challenge before submitting a code."
            );
        RequireAccountMarker(xml);
        var point = PFlowNativeLoginNavigator.Control(xml, "code");
        if (
            point == null
            || !await ld.IsAdbKeyboardActiveAsync(request.Binding.DeviceIndex, path, ct)
        )
            throw new InvalidOperationException("Verification input is unavailable.");
        if (!await ld.TapAsync(request.Binding.DeviceIndex, point.Value.X, point.Value.Y, path, ct))
            throw new InvalidOperationException("Verification field could not be selected.");
        xml = await Screen(request.Binding, ct);
        RequireAccountMarker(xml);
        if (
            PFlowLoginScreen.Classify(xml) != PFlowLoginScreenKind.AuthenticatorCodeRequired
            || !XDocument
                .Parse(xml)
                .Descendants("node")
                .Any(n =>
                    (string?)n.Attribute("package") == request.Binding.AppPackage
                    && (string?)n.Attribute("class") == "android.widget.EditText"
                    && ((string?)n.Attribute("content-desc"))?.Split(',')[0] == "Code"
                    && (string?)n.Attribute("focused") == "true"
                )
        )
            throw new InvalidOperationException(
                "Expected verification field did not receive focus."
            );
        try
        {
            if (
                !await ld.InputTextAdbKeyboardAsync(
                    request.Binding.DeviceIndex,
                    Encoding.ASCII.GetString(code.Value),
                    true,
                    path,
                    ct
                )
            )
                throw new InvalidOperationException("Verification input failed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException(
                "Verification input failed; no code submission was made."
            );
        }
        xml = await Screen(request.Binding, ct);
        RequireAccountMarker(xml);
        if (
            PFlowLoginScreen.Classify(xml) != PFlowLoginScreenKind.AuthenticatorCodeRequired
            || !CodeMatches(xml, code)
            || !code.IsUsable(DateTimeOffset.UtcNow, 6)
        )
            throw new InvalidOperationException(
                "Verification draft changed or expired; no code submission was made."
            );
        point = PFlowNativeLoginNavigator.Control(xml, "Continue");
        if (point == null)
            throw new InvalidOperationException("Verification submit control is unavailable.");
        _submitted = true; // Unknown tap outcomes must never result in another submission.
        if (!await ld.TapAsync(request.Binding.DeviceIndex, point.Value.X, point.Value.Y, path, ct))
            return PFlowOtpSubmission.Unknown;
        var until = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < until)
        {
            var kind = PFlowLoginScreen.Classify(await Screen(request.Binding, ct));
            if (kind == PFlowLoginScreenKind.SaveLoginInfo)
                return PFlowOtpSubmission.Confirmed;
            if (kind == PFlowLoginScreenKind.Checkpoint)
                return PFlowOtpSubmission.Unknown;
            await Task.Delay(500, ct);
        }
        return PFlowOtpSubmission.Unknown;
    }

    public static bool CodeMatches(string xml, PFlowOtpCode code)
    {
        try
        {
            var fields = XDocument
                .Parse(xml)
                .Descendants("node")
                .Where(n =>
                    (string?)n.Attribute("package") == "com.facebook.katana"
                    && (string?)n.Attribute("class") == "android.widget.EditText"
                    && ((string?)n.Attribute("content-desc"))?.Split(',')[0] == "Code"
                    && (string?)n.Attribute("enabled") == "true"
                )
                .ToArray();
            return !code.IsDisposed
                && fields.Length == 1
                && (string?)fields[0].Attribute("text") == Encoding.ASCII.GetString(code.Value);
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private void RequireAccountMarker(string xml)
    {
        var markers = XDocument
            .Parse(xml)
            .Descendants("node")
            .Where(n => (string?)n.Attribute("package") == attemptBinding.AppPackage)
            .Select(n =>
                (string?)n.Attribute("content-desc") is { Length: > 0 } desc
                    ? desc
                    : (string?)n.Attribute("text") ?? ""
            )
            .Where(label => label.EndsWith(" • Facebook", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (markers.Length != 1 || (_accountMarker != null && _accountMarker != markers[0]))
            throw new InvalidOperationException(
                "Verification account context changed or is unavailable."
            );
        _accountMarker ??= markers[0];
    }
}
