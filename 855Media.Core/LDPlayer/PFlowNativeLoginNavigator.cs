using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// Login form observed on LD-74, Facebook 581.0.0.45.58. No guessed coordinates.
public sealed class PFlowNativeLoginNavigator(
    ILDPlayerService ld,
    string path,
    Func<PFlowAccountBinding, CancellationToken, Task<PFlowAccountObservation>>? verify = null,
    IPFlowOtpChallengeAdapter? otpAdapter = null,
    IPFlowOtpCodeSource? otpCodes = null
) : IPFlowLoginNavigator
{
    public async Task<PFlowWorkerIdentity> ReadWorkerAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        var worker = (await ld.GetInstancesAsync(path, ct)).Single(i =>
            i.Index == binding.DeviceIndex
        );
        return new(
            worker.Index,
            worker.Name,
            PFlowDeviceRouting.ReadIdentity(path, worker.Index),
            binding.AppPackage
        );
    }

    public static (int X, int Y)? Control(string? xml, string role)
    {
        try
        {
            var nodes = FacebookHierarchyCache
                .ParseStrict(xml ?? "")
                .Descendants("node")
                .Where(n =>
                    (string?)n.Attribute("package") == "com.facebook.katana"
                    && (string?)n.Attribute("enabled") == "true"
                );
            var matches = nodes
                .Where(n =>
                    role switch
                    {
                        "login" => (string?)n.Attribute("class") == "android.widget.EditText"
                            && (string?)n.Attribute("password") != "true"
                            && ((string?)n.Attribute("content-desc"))?.StartsWith(
                                "Mobile number or email,",
                                StringComparison.Ordinal
                            ) == true,
                        "password" => (string?)n.Attribute("class") == "android.widget.EditText"
                            && (string?)n.Attribute("password") == "true"
                            && ((string?)n.Attribute("content-desc"))?.StartsWith(
                                "Password,",
                                StringComparison.Ordinal
                            ) == true,
                        "code" => (string?)n.Attribute("class") == "android.widget.EditText"
                            && ((string?)n.Attribute("content-desc"))?.Split(',')[0] == "Code",
                        _ => (string?)n.Attribute("clickable") == "true"
                            && (
                                string.Equals(
                                    (string?)n.Attribute("content-desc"),
                                    role,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                || string.Equals(
                                    (string?)n.Attribute("text"),
                                    role,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            ),
                    }
                )
                .ToArray();
            if (matches.Length != 1)
                return null;
            var bounds = Regex.Match(
                (string?)matches[0].Attribute("bounds") ?? "",
                @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
            );
            if (!bounds.Success)
                return null;
            int left = int.Parse(bounds.Groups[1].Value),
                top = int.Parse(bounds.Groups[2].Value),
                right = int.Parse(bounds.Groups[3].Value),
                bottom = int.Parse(bounds.Groups[4].Value);
            return right > left && bottom > top ? ((left + right) / 2, (top + bottom) / 2) : null;
        }
        catch (Exception ex)
            when (ex is System.Xml.XmlException or FormatException or OverflowException)
        {
            return null;
        }
    }

    public async Task<PFlowAccountObservation> LoginAsync(
        PFlowAccountBinding binding,
        PFlowCredential credential,
        Func<string, Task> progress,
        CancellationToken ct
    )
    {
        if (binding.AppPackage != "com.facebook.katana")
            throw new NotSupportedException("The inspected login adapter supports FB-01 only.");
        var index = binding.DeviceIndex;
        var preparation = new PFlowSessionPreparation(ld, path);
        await preparation.PrepareAsync(binding, progress, ct);
        if (otpAdapter is PFlowNativeOtpChallengeAdapter nativeOtp)
            nativeOtp.BindNetworkIdentity(preparation.NetworkIdentity!);
        await Task.Delay(1500, ct);
        async Task<string> Screen()
        {
            await preparation.RequireConnectionAsync(binding, ct);
            var worker = await ReadWorkerAsync(binding, ct);
            binding.RequireWorker(
                worker.DeviceIndex,
                worker.DeviceName,
                worker.InstanceIdentity,
                worker.AppPackage
            );
            return await preparation.ReadFacebookScreenAsync(binding, ct);
        }
        async Task Tap(string role)
        {
            var current = await Screen();
            if (role == "Log in" && !CanSubmitLogin(current, credential))
                throw new InvalidOperationException(
                    "Login fields changed or could not be verified. No login submission was made."
                );
            var point = Control(current, role);
            if (point == null || !await ld.TapAsync(index, point.Value.X, point.Value.Y, path, ct))
                throw new InvalidOperationException(
                    "The inspected Facebook login control is unavailable. Open its login screen and retry."
                );
        }
        async Task Input(string role, byte[] value)
        {
            await Tap(role);
            var current = XDocument.Parse(await Screen()).Descendants("node");
            var focused = current
                .Where(n =>
                    (string?)n.Attribute("package") == binding.AppPackage
                    && (string?)n.Attribute("class") == "android.widget.EditText"
                    && (string?)n.Attribute("focused") == "true"
                )
                .ToArray();
            var expectedPassword = role == "password";
            if (
                focused.Length != 1
                || ((string?)focused[0].Attribute("password") == "true") != expectedPassword
            )
                throw new InvalidOperationException(
                    "Expected login field did not receive focus. No login submission was made."
                );
            try
            {
                if (
                    !await ld.InputTextAdbKeyboardAsync(
                        index,
                        Encoding.UTF8.GetString(value),
                        true,
                        path,
                        ct
                    )
                )
                    throw new InvalidOperationException();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                throw new InvalidOperationException(
                    "Login text entry failed. No login submission was made."
                );
            }
        }
        var xml = await Screen();
        var screenKind = PFlowLoginScreen.Classify(xml, binding.AppPackage);
        if (
            screenKind
            is PFlowLoginScreenKind.Checkpoint
                or PFlowLoginScreenKind.CredentialsRejected
        )
            throw new InvalidOperationException(PFlowLoginScreen.Message(screenKind));
        if (screenKind == PFlowLoginScreenKind.OwnPersonalProfile)
        {
            if (verify == null)
                throw new NotSupportedException(
                    "A personal session already exists. Exact live UID verification is required to reuse it; credentials were not entered."
                );
            return await verify(binding, ct);
        }
        if (Control(xml, "I already have a profile") != null)
        {
            await Tap("I already have a profile");
            await Task.Delay(1000, ct);
            xml = await Screen();
        }
        if (
            Control(xml, "login") == null
            || Control(xml, "password") == null
            || Control(xml, "Log in") == null
        )
            throw new InvalidOperationException(
                "Open Facebook’s Mobile number or email / Password login screen on this LD. Existing accounts were not logged out."
            );
        if (verify == null)
            throw new NotSupportedException(
                "LD login form confirmed. The installed authenticator adapter is available, but exact live personal UID verification is still required before automatic login can be enabled. Credentials were not entered."
            );
        if (
            (otpAdapter == null) != (otpCodes == null)
            || (credential.TwoFactorKind != PFlowTwoFactorKind.None && otpAdapter == null)
        )
            throw new NotSupportedException(
                "A supported verification-code adapter and code source are required for this login."
            );
        await preparation.EnsureTextInputAsync(binding, progress, ct);
        // Secret input is never put in screenshots, progress messages or exception text.
        await progress("Entering login details into the inspected Facebook form.");
        await Input("login", credential.LoginIdentifier);
        await Input("password", credential.Password);
        xml = await Screen();
        if (Control(xml, "login") == null || Control(xml, "password") == null)
            throw new InvalidOperationException(
                "The login form changed before submission; no login tap was made."
            );
        await Tap("Log in");
        await progress("Login submitted once. Waiting for Facebook verification.");
        if (otpAdapter != null && otpCodes != null)
            await new PFlowOtpAuthenticationService(otpAdapter, otpCodes).AuthenticateAsync(
                binding,
                credential,
                progress,
                ct
            );
        xml = await Screen();
        if (PFlowLoginScreen.Classify(xml) == PFlowLoginScreenKind.SaveLoginInfo)
        {
            await Tap("Not now");
            await Task.Delay(500, ct);
        }
        else if (PFlowLoginScreen.Classify(xml) == PFlowLoginScreenKind.Checkpoint)
            throw new InvalidOperationException(
                PFlowLoginScreen.Message(PFlowLoginScreenKind.Checkpoint)
            );
        return await verify(binding, ct);
    }

    public static bool CanSubmitLogin(string xml, PFlowCredential credential)
    {
        if (
            credential.IsDisposed
            || Control(xml, "login") == null
            || Control(xml, "password") == null
            || Control(xml, "Log in") == null
        )
            return false;
        try
        {
            var fields = XDocument
                .Parse(xml)
                .Descendants("node")
                .Where(n =>
                    (string?)n.Attribute("package") == "com.facebook.katana"
                    && (string?)n.Attribute("class") == "android.widget.EditText"
                    && (string?)n.Attribute("enabled") == "true"
                )
                .ToArray();
            if (fields.Length != 2)
                return false;
            var login = fields.Single(n => (string?)n.Attribute("password") != "true");
            var password = fields.Single(n => (string?)n.Attribute("password") == "true");
            var passwordText = (string?)password.Attribute("text") ?? "";
            var expected = Encoding.UTF8.GetString(credential.Password);
            // A mask verifies length only; never describe this as an exact password round-trip.
            return (string?)login.Attribute("text")
                    == Encoding.UTF8.GetString(credential.LoginIdentifier)
                && (
                    passwordText == expected
                    || (
                        passwordText.Length == expected.Length
                        && Regex.IsMatch(passwordText, @"^[•*●·]+$")
                    )
                );
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return false;
        }
    }
}
