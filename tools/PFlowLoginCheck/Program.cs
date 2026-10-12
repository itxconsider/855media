using System.Text.Json;
using _855Media.Core.LDPlayer;

// A narrow opt-in live check: no publisher, media transfer, or ScreenTime is instantiated.
if (
    args.Length < 3
    || args[0]
        is not (
            "--inspect"
            or "--snapshot"
            or "--logout-login"
            or "--finish-login"
            or "--recover-login"
        )
    || !int.TryParse(args[1], out var index)
    || index < 0
    || args[0] is "--logout-login" or "--finish-login" or "--recover-login"
        && !args.Contains("--live")
)
{
    Console.Error.WriteLine(
        "Usage: PFlowLoginCheck --inspect <index> <LD folder> | --logout-login <index> <LD folder> --live"
    );
    return 2;
}

var ldPath = Path.GetFullPath(args[2]);
var reportDirectory = Path.GetFullPath(
    Path.Combine(
        AppContext.BaseDirectory,
        "reports",
        "ld-" + index + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")
    )
);
Directory.CreateDirectory(reportDirectory);
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var ct = cancellation.Token;
using var atx = new AtxAgentClient { IsTapTracingEnabled = false };
var ld = new LDPlayerService(atx);
var stage = "binding";
string? ownerUid = null;
string? beforeUid = null;
string? afterUid = null;
var loggedOut = false;
var succeeded = false;
var startedLoggedOut = false;
var passwordSubmissions = 0;
var otpSubmissions = 0;

Task Progress(string message)
{
    if (message == "Login submitted once. Waiting for Facebook verification.")
        passwordSubmissions++;
    if (message == "Submitting one code to the confirmed verification challenge.")
        otpSubmissions++;
    Console.WriteLine(message);
    return Task.CompletedTask;
}

async Task Capture(string label)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var xml = await ld.DumpUiHierarchyAsync(index, ldPath, timeout.Token);
    if (!string.IsNullOrWhiteSpace(xml))
    {
        await File.WriteAllTextAsync(
            Path.Combine(reportDirectory, label + ".xml"),
            PFlowUiPrivacy.RedactForDiagnostics(xml),
            timeout.Token
        );
        Console.WriteLine(
            "Screen: "
                + PFlowLoginScreen.Classify(xml)
                + "; editable input: "
                + PFlowUiPrivacy.ContainsInput(xml)
        );
    }
}

async Task RequireIdle(CancellationToken token)
{
    var jobs = await new PFlowHistoryStore().ReadAsync(token);
    if (
        jobs.Any(j =>
            j.DeviceIndex == index
            && j.State is PFlowState.Unknown or PFlowState.SubmitRequested or PFlowState.Submitted
        )
    )
        throw new InvalidOperationException("Review uncertain publication history before logout.");
    if (new ScreenTimeHistoryStore().NeedsReview(index))
        throw new InvalidOperationException(
            "Review unresolved ScreenTime interactions before logout."
        );
    var notifications = await ld.ExecuteAdbAsync(
        index,
        "shell dumpsys notification --noredact",
        ldPath,
        token
    );
    if (PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing))
        throw new InvalidOperationException("Facebook has an ongoing media upload.");
}

try
{
    var accounts = await new PFlowAccountStore().LoadAsync(ct);
    var candidates = accounts
        .Where(a =>
            a.Binding.DeviceIndex == index
            && a.Binding.AppPackage == "com.facebook.katana"
            && (
                args[0] is not ("--logout-login" or "--recover-login")
                || !string.IsNullOrWhiteSpace(a.CredentialReference)
            )
        )
        .ToArray();
    if (candidates.Length != 1)
        throw new InvalidOperationException(
            "The check requires one unambiguous mapped account on the requested LD; logout/login also requires protected credentials."
        );
    var account = candidates[0];
    var binding = account.Binding;
    ownerUid = binding.OwnerUid;
    PFlowAccountStore.RequireCurrentBinding(
        binding,
        await ld.GetInstancesAsync(ldPath, ct),
        ldPath
    );
    if (args[0] is "--logout-login" or "--recover-login")
        using (
            var credential = await new PFlowCredentialStore().LoadAsync(
                binding,
                account.CredentialReference,
                ct
            )
        )
            Console.WriteLine(
                "Protected credentials loaded for LD-"
                    + index
                    + "; factor: "
                    + credential.TwoFactorKind
                    + ". Values hidden."
            );
    using var lease = await PFlowDeviceLease.AcquireAsync(index, ct);
    await RequireIdle(ct);
    stage = "preparation";
    await new PFlowSessionPreparation(ld, ldPath).PrepareAsync(binding, Progress, ct);
    await Capture("initial");
    if (args[0] == "--snapshot")
    {
        stage = "snapshot";
        succeeded = true;
        return 0;
    }
    var navigator = new PFlowNativeAccountNavigator(
        ld,
        ldPath,
        b => accounts.FirstOrDefault(a => a.Binding == b)?.Name ?? b.OwnerUid
    );
    if (args[0] == "--inspect")
    {
        stage = "observe-before";
        var before = await navigator.ObserveAsync(binding, ct);
        beforeUid = before.OwnerUid;
        Console.WriteLine("Before: " + before.State + "; live UID: " + before.OwnerUid);
        await Capture("before");
        succeeded = PFlowAutomaticSessionService.Verified(before, binding);
        return succeeded ? 0 : 1;
    }
    if (args[0] == "--finish-login")
    {
        stage = "finish-login";
        var preparation = new PFlowSessionPreparation(ld, ldPath);
        var pending = await preparation.ReadFacebookScreenAsync(binding, ct);
        var pendingKind = PFlowLoginScreen.Classify(pending);
        if (pendingKind == PFlowLoginScreenKind.SaveLoginInfo)
        {
            var point = PFlowNativeLoginNavigator.Control(pending, "Not now");
            if (
                point == null
                || !await ld.TapAsync(index, point.Value.X, point.Value.Y, ldPath, ct)
            )
                throw new InvalidOperationException("Save-login prompt could not be dismissed.");
        }
        else if (
            PFlowLoginScreen.RequiresAuthentication(pendingKind)
            || PFlowNativeAccountNavigator.LoginLanding(pending)
        )
            throw new InvalidOperationException(
                "Interrupted login still requires authentication; no password was resubmitted."
            );
        await Task.Delay(750, ct);
        var observed = await navigator.ObserveAsync(binding, ct);
        afterUid = observed.OwnerUid;
        succeeded = PFlowAutomaticSessionService.Verified(observed, binding);
        await Capture("after");
        Console.WriteLine(
            "After: "
                + observed.State
                + "; live UID: "
                + afterUid
                + "; matches stored binding: "
                + succeeded
        );
        stage = succeeded ? "complete" : "finish-login";
        return succeeded ? 0 : 1;
    }
    if (args[0] != "--recover-login")
    {
        Console.WriteLine(
            "Explicit current-account logout requested on LD-"
                + index
                + ". Post-login UID will be verified."
        );
        await RequireIdle(ct);
        stage = "logout";
        var current = await new PFlowSessionPreparation(ld, ldPath).ReadFacebookScreenAsync(
            binding,
            ct
        );
        startedLoggedOut =
            PFlowLoginScreen.Classify(current) == PFlowLoginScreenKind.LoginRequired
            || PFlowNativeAccountNavigator.LoginLanding(current);
        if (PFlowLoginScreen.Classify(current) == PFlowLoginScreenKind.LoginRequired)
        {
            Console.WriteLine("Continuing the requested check from the logged-out password form.");
        }
        else
            await navigator.LogoutAndOpenLoginAsync(binding, ct);
        var loginScreen = await ld.DumpUiHierarchyAsync(index, ldPath, ct);
        if (PFlowLoginScreen.Classify(loginScreen) != PFlowLoginScreenKind.LoginRequired)
            throw new InvalidOperationException(
                "Logout did not reach the recognized password form."
            );
        loggedOut = true;
        Console.WriteLine("Logout confirmed: recognized password form.");
        await Capture("logged-out");
    }
    // Authorization is for this check only; saved selections and options are unchanged.
    account.AllowAutomaticLogin = true;
    stage = "login-recovery";
    var recovery = new PFlowAutomaticSessionService(
        navigator,
        new PFlowCredentialStore(),
        RequireIdle
    );
    await recovery.EnsureAsync(account, Progress, ct, deviceLeaseAlreadyHeld: true);
    stage = "observe-after";
    var after = await navigator.ObserveAsync(binding, ct);
    afterUid = after.OwnerUid;
    succeeded = PFlowAutomaticSessionService.Verified(after, binding);
    await Capture("after");
    Console.WriteLine(
        "After: "
            + after.State
            + "; live UID: "
            + after.OwnerUid
            + "; matches stored binding: "
            + succeeded
    );
    if (!succeeded)
        throw new InvalidOperationException("Post-login personal UID verification did not pass.");
    stage = "complete";
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Check stopped at " + stage + ": " + ex.GetType().Name);
    // Adapter messages are fixed/non-secret. Other device/library exception text is suppressed.
    if (ex is InvalidOperationException or NotSupportedException)
        Console.Error.WriteLine(ex.Message);
    try
    {
        await Capture("stopped");
    }
    catch { }
    return 1;
}
finally
{
    await File.WriteAllTextAsync(
        Path.Combine(reportDirectory, "result.json"),
        JsonSerializer.Serialize(
            new
            {
                DeviceIndex = index,
                Mode = args[0],
                ExpectedOwnerUid = ownerUid,
                BeforeUid = beforeUid,
                AfterUid = afterUid,
                Stage = stage,
                LogoutConfirmed = loggedOut,
                StartedLoggedOut = startedLoggedOut,
                PasswordSubmissions = passwordSubmissions,
                OtpSubmissions = otpSubmissions,
                Success = succeeded,
                PublishingActions = 0,
                FinishedUtc = DateTimeOffset.UtcNow,
            },
            new JsonSerializerOptions { WriteIndented = true }
        )
    );
    Console.WriteLine("Evidence: " + reportDirectory);
}
