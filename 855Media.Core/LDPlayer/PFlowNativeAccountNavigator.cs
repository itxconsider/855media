using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// All navigation uses fresh native bounds. No private account cache is identity evidence.
public sealed class PFlowNativeAccountNavigator(
    ILDPlayerService ld,
    string path,
    Func<PFlowAccountBinding, string> name,
    Func<PFlowOtpRequest, CancellationToken, Task<PFlowOtpCode>>? receiveCode = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null
) : IPFlowAutomaticAccountNavigator
{
    private readonly PFlowSessionPreparation _preparation = new(ld, path);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private PFlowAccountBinding? _lastPersonalBinding;
    private PFlowAccountObservation? _lastPersonalObservation;

    public Task<PFlowWorkerIdentity> ReadWorkerAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    ) => _preparation.RequireWorkerAsync(binding, ct);

    private async Task<string> Screen(PFlowAccountBinding binding, CancellationToken ct)
    {
        if (binding.AppPackage != "com.facebook.katana")
            throw new NotSupportedException("Automatic accounts currently support FB-01 only.");
        return await _preparation.ReadFacebookScreenAsync(binding, ct);
    }

    public static (int X, int Y)? FindControl(string? xml, params string[] labels)
    {
        try
        {
            var matches = XDocument
                .Parse(xml ?? "")
                .Descendants("node")
                .Where(n =>
                    (string?)n.Attribute("package") == "com.facebook.katana"
                    && (string?)n.Attribute("enabled") == "true"
                    && (string?)n.Attribute("clickable") == "true"
                    && labels.Any(l =>
                        string.Equals(
                            (string?)n.Attribute("text"),
                            l,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            (string?)n.Attribute("content-desc"),
                            l,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                .Select(n => ClickPoint(n, xml ?? ""))
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        catch (Exception ex)
            when (ex is System.Xml.XmlException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static bool Has(string xml, string label) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("package") == "com.facebook.katana"
                && (
                    (string?)n.Attribute("text") == label
                    || (string?)n.Attribute("content-desc") == label
                )
            );

    private static (int X, int Y)? ClickPoint(XElement node, string xml)
    {
        if (
            !string.Equals(
                (string?)node.Attribute("text"),
                "Log out",
                StringComparison.OrdinalIgnoreCase
            )
            && !string.Equals(
                (string?)node.Attribute("content-desc"),
                "Log out",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return ScreenTimeHierarchy.Center(node);
        var bounds = Regex.Match(
            (string?)node.Attribute("bounds") ?? "",
            @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
        );
        if (!bounds.Success)
            return null;
        var left = int.Parse(bounds.Groups[1].Value);
        var top = int.Parse(bounds.Groups[2].Value);
        var right = int.Parse(bounds.Groups[3].Value);
        var bottom = int.Parse(bounds.Groups[4].Value);
        if (right <= left || bottom <= top)
            return null;
        var (_, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
        var tabs = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n =>
                (string?)n.Attribute("package") == "com.facebook.katana"
                && (string?)n.Attribute("enabled") == "true"
                && (string?)n.Attribute("clickable") == "true"
                && Regex.IsMatch((string?)n.Attribute("content-desc") ?? "", @"^.+, tab \d+ of \d+")
            )
            .Select(n =>
                Regex.Match(
                    (string?)n.Attribute("bounds") ?? "",
                    @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$"
                )
            )
            .Where(m => m.Success && int.Parse(m.Groups[2].Value) >= height / 2)
            .ToArray();
        if (tabs.Length >= 2)
            bottom = Math.Min(bottom, tabs.Min(m => int.Parse(m.Groups[2].Value)));
        if (bottom - top < Math.Min(32, (int.Parse(bounds.Groups[4].Value) - top) / 2))
            return null;
        return ((left + right) / 2, (top + bottom) / 2);
    }

    public static bool OwnProfile(string xml) =>
        !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !PFlowUiPrivacy.ContainsInput(xml)
        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        && !AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(xml))
        && (
            FindControl(xml, "Edit profile") != null
            || FindControl(xml, "Edit cover photo button") != null
                && FindControl(xml, "Edit profile picture") != null
        )
        && !Has(xml, "Manage Page");

    // Only the link in a personal profile's own settings is accepted, never a feed/public URL.
    public static string? ReadOwnProfileUid(string? xml)
    {
        if (
            PFlowUiPrivacy.ContainsInput(xml)
            || PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        )
            return null;
        var nodes = ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n => (string?)n.Attribute("package") == "com.facebook.katana")
            .ToArray();
        var labels = nodes
            .SelectMany(n =>
                new[]
                {
                    (string?)n.Attribute("text") ?? "",
                    (string?)n.Attribute("content-desc") ?? "",
                }
            )
            .ToArray();
        if (
            !labels.Contains("Profile settings", StringComparer.OrdinalIgnoreCase)
            || !labels.Contains("Your profile link", StringComparer.OrdinalIgnoreCase)
            || labels.Contains("Your Page link", StringComparer.OrdinalIgnoreCase)
            || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        )
            return null;
        var urls = labels
            .SelectMany(l =>
                Regex
                    .Matches(l, @"https://(?:www\.|m\.)?facebook\.com/[^\s,<>""']+")
                    .Select(m => m.Value)
            )
            .Distinct()
            .ToArray();
        // Reject vanity URLs and extra links rather than borrowing a UID from cached root records.
        return urls.Length == 1 ? ReadNumericProfileLinkUid(urls[0]) : null;
    }

    public static string? ReadNumericProfileLinkUid(string? link)
    {
        if (
            !Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || !url.IsDefaultPort
            || !string.IsNullOrEmpty(url.UserInfo)
            || url.Host is not ("facebook.com" or "www.facebook.com" or "m.facebook.com")
        )
            return null;
        string? uid = null;
        if (url.AbsolutePath == "/profile.php")
        {
            var ids = url
                .Query.TrimStart('?')
                .Split('&')
                .Where(p => p.StartsWith("id=", StringComparison.Ordinal))
                .ToArray();
            if (ids.Length == 1)
                uid = ids[0][3..];
        }
        else
        {
            var match = Regex.Match(url.AbsolutePath, @"^/(?:people/[^/]+/)?(\d+)/?$");
            if (match.Success)
                uid = match.Groups[1].Value;
        }
        return !string.IsNullOrEmpty(uid) && uid.Length <= 32 && uid.All(char.IsAsciiDigit)
            ? uid
            : null;
    }

    public static (int X, int Y)? CopyProfileLinkControl(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        && (Has(xml, "Profile settings") || Has(xml, "Profile Settings"))
        && !Has(xml, "Page Settings")
        && !Has(xml, "Your Page link")
        && !Has(xml, "Copy Page link")
            ? FindControl(xml, "Copy profile link")
            : null;

    public static bool LoginLanding(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && !Has(xml, "Edit profile")
        && (
            FindControl(
                xml,
                "Log into another account",
                "Log in to another account",
                "I already have a profile"
            ) != null
            || Has(xml, "Facebook from Meta")
                && FindControl(xml, "Use another profile") != null
                && FindControl(xml, "Create new account") != null
        );

    private static PFlowAuthenticationState Authentication(string xml) =>
        LoginLanding(xml)
            ? PFlowAuthenticationState.LoginRequired
            : PFlowLoginScreen.Classify(xml) switch
            {
                PFlowLoginScreenKind.LoginRequired => PFlowAuthenticationState.LoginRequired,
                PFlowLoginScreenKind.Checkpoint => PFlowAuthenticationState.Checkpoint,
                PFlowLoginScreenKind.CredentialsRejected =>
                    PFlowAuthenticationState.VerificationFailed,
                _ => PFlowAuthenticationState.Unavailable,
            };

    public async Task<PFlowAccountObservation> ObserveAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        var observed = await ObserveCoreAsync(binding, ct, true);
        if (observed.State != PFlowAuthenticationState.Unavailable)
            return observed;

        // A Page can expose the same Edit profile controls as a personal profile.
        // Failed live identity proof gets one bounded recovery to the mapped personal actor.
        // Navigation labels choose a destination; only a fresh settings UID can authenticate it.
        var xml = await Screen(binding, ct);
        var personalName = name(binding);
        if (!CanRecoverPersonalProfile(xml) || string.IsNullOrWhiteSpace(personalName))
            return observed;
        if (PersonalSettings(xml) || PageSettings(xml) || Has(xml, "Your Page link"))
        {
            if (!await ld.SendKeyAsync(binding.DeviceIndex, 4, path, ct))
                return observed;
            await _delay(TimeSpan.FromMilliseconds(400), ct);
            xml = await Screen(binding, ct);
            if (!CanRecoverPersonalProfile(xml))
                return observed;
        }
        if (
            !await ld.EnsureSwitchedToPersonalProfileAsync(
                binding.DeviceIndex,
                personalName,
                path,
                ct
            )
        )
            return observed;
        await _delay(TimeSpan.FromMilliseconds(500), ct);
        return await ObserveCoreAsync(binding, ct, false);
    }

    public static bool CanRecoverPersonalProfile(string xml) =>
        !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !LoginLanding(xml)
        && !PFlowUiPrivacy.ContainsInput(xml)
        && !PFlowAutoPostService.IsComposer(xml, "Reel")
        && (
            OwnProfile(xml)
            || PersonalSettings(xml)
            || PageSettings(xml)
            || Has(xml, "Your Page link")
            || MenuSurface(xml)
        );

    private async Task<PFlowAccountObservation> ObserveCoreAsync(
        PFlowAccountBinding binding,
        CancellationToken ct,
        bool recoverPage
    )
    {
        var xml = await Screen(binding, ct);
        if (PFlowLoginScreen.Classify(xml) == PFlowLoginScreenKind.LocationServicesIntro)
            xml = await new PFlowNativeLoginOnboarding(ld, path, _delay).CompleteLocationAsync(
                binding,
                ct
            );
        PFlowAccountObservation Missing(string current) =>
            new(
                "",
                binding.AppPackage,
                false,
                false,
                false,
                DateTimeOffset.UtcNow,
                Authentication(current)
            );
        if (recoverPage && PageSettings(xml))
        {
            if (await ReturnFromPageSettingsAsync(binding, ct))
                return await ObserveCoreAsync(binding, ct, false);
            return Missing(xml);
        }
        if (
            recoverPage
            && !PFlowUiPrivacy.ContainsInput(xml)
            && (Has(xml, "About") && Has(xml, "Personal details") || PersonalSettings(xml))
            && FindControl(xml, "Back") != null
        )
        {
            if (!await ld.SendKeyAsync(binding.DeviceIndex, 4, path, ct))
                return Missing(xml);
            await _delay(TimeSpan.FromMilliseconds(500), ct);
            return await ObserveCoreAsync(binding, ct, false);
        }
        if (
            PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            || LoginLanding(xml)
        )
            return Missing(xml);
        if (PFlowUiPrivacy.ContainsInput(xml) || PFlowAutoPostService.IsComposer(xml, "Reel"))
            return Missing(xml);
        if (!OwnProfile(xml))
        {
            // Menu may expose the active actor's name even when a different login is requested.
            var activeName = ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n => (string?)n.Attribute("package") == binding.AppPackage)
                .Select(n => (string?)n.Attribute("content-desc") ?? "")
                .FirstOrDefault(l => l.EndsWith(", see your profile", StringComparison.Ordinal));
            var currentName = activeName == null ? name(binding) : activeName[..^18];
            if (string.IsNullOrWhiteSpace(currentName))
                currentName = binding.OwnerUid;
            var opened = await PFlowOwnProfileNavigation.OpenAsync(
                ld,
                binding.DeviceIndex,
                currentName,
                path,
                s => OwnProfile(s.RawXml ?? ""),
                ct,
                delay: _delay,
                navigateCurrentActor: true
            );
            if (!opened)
                return Missing(await Screen(binding, ct));
        }
        xml = await Screen(binding, ct);
        if (!OwnProfile(xml))
            return Missing(xml);
        var settings = FindControl(
            xml,
            "Profile settings",
            "More profile settings",
            "More options"
        );
        if (settings == null)
            return Missing(xml);
        // Re-read the own surface immediately before using its navigation bounds.
        xml = await Screen(binding, ct);
        settings = OwnProfile(xml)
            ? FindControl(xml, "Profile settings", "More profile settings", "More options")
            : null;
        if (
            settings == null
            || !await ld.TapAsync(binding.DeviceIndex, settings.Value.X, settings.Value.Y, path, ct)
        )
            return Missing(xml);
        string? uid = null;
        var copyAttempted = false;
        for (var i = 0; i < 12; i++)
        {
            await _delay(TimeSpan.FromMilliseconds(400), ct);
            xml = await Screen(binding, ct);
            uid = ReadOwnProfileUid(xml);
            if (uid == null && !copyAttempted && CopyProfileLinkControl(xml) != null)
            {
                copyAttempted = true;
                if (await ld.PrepareProfileLinkClipboardAsync(binding.DeviceIndex, path, ct))
                {
                    // Reset succeeded. Re-read the settings before tapping their Copy control.
                    xml = await Screen(binding, ct);
                    var copy = CopyProfileLinkControl(xml);
                    if (
                        copy != null
                        && await ld.TapAsync(
                            binding.DeviceIndex,
                            copy.Value.X,
                            copy.Value.Y,
                            path,
                            ct
                        )
                    )
                    {
                        for (var poll = 0; poll < 5 && uid == null; poll++)
                        {
                            await _delay(TimeSpan.FromMilliseconds(200), ct);
                            xml = await Screen(binding, ct);
                            if (CopyProfileLinkControl(xml) == null)
                                break;
                            var copiedLink = await ld.ReadCopiedProfileLinkAsync(
                                binding.DeviceIndex,
                                path,
                                ct
                            );
                            if (copiedLink != null)
                            {
                                using var resolver = new PFlowProfileLinkResolver();
                                uid = await resolver.ResolveUidAsync(copiedLink, ct);
                                break;
                            }
                        }
                    }
                }
            }
            if (
                uid != null
                || PageSettings(xml)
                || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
            )
                break;
            if (
                i % 3 == 2
                && PersonalSettings(xml)
                && !HasProfileUrl(xml)
                && CopyProfileLinkControl(xml) == null
            )
            {
                var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(xml);
                if (
                    width <= 0
                    || height <= 0
                    || !await ld.SwipeAsync(
                        binding.DeviceIndex,
                        width / 2,
                        height * 4 / 5,
                        width / 2,
                        height / 3,
                        400,
                        path,
                        ct
                    )
                )
                    break;
            }
        }
        if (uid == null)
        {
            if (recoverPage && (Has(xml, "Your Page link") || PageSettings(xml)))
            {
                if (await ReturnFromPageSettingsAsync(binding, ct))
                    return await ObserveCoreAsync(binding, ct, false);
            }
            return Missing(xml);
        }
        if (!await ld.SendKeyAsync(binding.DeviceIndex, 4, path, ct))
            return Missing(xml);
        await _delay(TimeSpan.FromMilliseconds(400), ct);
        xml = await Screen(binding, ct);
        if (!OwnProfile(xml))
            return Missing(xml);
        _lastPersonalBinding = binding with { OwnerUid = uid };
        var observation = new PFlowAccountObservation(
            uid,
            binding.AppPackage,
            true,
            true,
            true,
            DateTimeOffset.UtcNow,
            PFlowAuthenticationState.Authenticated
        );
        _lastPersonalObservation = observation;
        return observation;
    }

    public static bool PageSettings(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && Has(xml, "Page Settings")
        && FindControl(xml, "Profile switcher, Double tap to open a list of your profiles") != null;

    private static bool PersonalSettings(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && (Has(xml, "Profile settings") || Has(xml, "Profile Settings"))
        && FindControl(xml, "Back") != null;

    private static bool HasProfileUrl(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Where(n => (string?)n.Attribute("package") == "com.facebook.katana")
            .Any(n =>
                Regex.IsMatch(
                    ((string?)n.Attribute("text") ?? "")
                        + " "
                        + ((string?)n.Attribute("content-desc") ?? ""),
                    @"https://(?:www\.|m\.)?facebook\.com/"
                )
            );

    private async Task<bool> ReturnFromPageSettingsAsync(
        PFlowAccountBinding binding,
        CancellationToken ct
    )
    {
        if (!await ld.SendKeyAsync(binding.DeviceIndex, 4, path, ct))
            return false;
        await _delay(TimeSpan.FromMilliseconds(400), ct);
        var personalName =
            _lastPersonalBinding == null ? name(binding) : name(_lastPersonalBinding);
        // Root metadata suggests a navigation destination, never an authenticated UID proof.
        if (_lastPersonalBinding == null)
        {
            var root = await ld.ExtractFacebookAccountsFromRootShellAsync(
                binding.DeviceIndex,
                binding.AppPackage,
                path,
                ct
            );
            var owners = root.Where(a => a.IsActive)
                .Select(a => string.IsNullOrWhiteSpace(a.UserUid) ? a.Uid : a.UserUid)
                .Distinct()
                .ToArray();
            if (owners.Length == 1)
                personalName =
                    root.SingleOrDefault(a => a.Type == "Profile" && a.Uid == owners[0])?.Name
                    ?? personalName;
        }
        return await ld.EnsureSwitchedToPersonalProfileAsync(
            binding.DeviceIndex,
            personalName,
            path,
            ct
        );
    }

    private async Task<bool> Tap(
        PFlowAccountBinding binding,
        CancellationToken ct,
        params string[] labels
    )
    {
        var xml = await Screen(binding, ct);
        var point = FindControl(xml, labels);
        return point != null
            && await ld.TapAsync(binding.DeviceIndex, point.Value.X, point.Value.Y, path, ct);
    }

    public Task OpenLoginAsync(
        PFlowAccountBinding binding,
        bool allowLogout,
        CancellationToken ct
    ) => OpenLoginCoreAsync(binding, allowLogout, false, ct);

    // Explicit logout/recovery checks must exercise logout even for the same account.
    public Task LogoutAndOpenLoginAsync(PFlowAccountBinding binding, CancellationToken ct) =>
        OpenLoginCoreAsync(binding, true, true, ct);

    private async Task OpenLoginCoreAsync(
        PFlowAccountBinding binding,
        bool allowLogout,
        bool forceLogout,
        CancellationToken ct
    )
    {
        var xml = await Screen(binding, ct);
        if (PFlowLoginScreen.Classify(xml) == PFlowLoginScreenKind.LoginRequired)
        {
            if (forceLogout)
                throw new InvalidOperationException(
                    "Facebook is already logged out; no logout action was issued."
                );
            return;
        }
        // The save-info dialog can appear after Log out, including after an interrupted run.
        // Dismiss it once and wait for the landing screen without issuing another logout.
        var dismissedSaveInfo = false;
        var pendingLogout =
            LogoutConfirmation(xml)
            || PFlowLoginScreen.Classify(xml) == PFlowLoginScreenKind.SaveLoginInfo;
        if (pendingLogout && !allowLogout)
            throw new InvalidOperationException(
                "A pending logout prompt requires account rotation permission."
            );
        if (PFlowLoginScreen.Classify(xml) == PFlowLoginScreenKind.SaveLoginInfo)
        {
            if (!allowLogout)
                throw new InvalidOperationException(
                    "A pending logout prompt requires account rotation permission."
                );
            if (!await Tap(binding, ct, "Not now"))
                throw new InvalidOperationException("Save-login prompt could not be dismissed.");
            dismissedSaveInfo = true;
            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }
        if (allowLogout && !pendingLogout && !LoginLanding(xml))
        {
            if (!forceLogout)
            {
                var previousActor = _lastPersonalBinding;
                var actor =
                    _lastPersonalObservation != null
                    && PFlowAutomaticSessionService.Verified(
                        _lastPersonalObservation,
                        binding with
                        {
                            OwnerUid = _lastPersonalObservation.OwnerUid,
                        }
                    )
                        ? _lastPersonalObservation
                        : await ObserveCoreAsync(binding, ct, false);
                if (
                    string.IsNullOrWhiteSpace(actor.OwnerUid)
                    || !PFlowAutomaticSessionService.Verified(
                        actor,
                        binding with
                        {
                            OwnerUid = actor.OwnerUid,
                        }
                    )
                    || previousActor != null && actor.OwnerUid != previousActor.OwnerUid
                )
                    throw new InvalidOperationException(
                        "Active personal identity changed before logout; account rotation stopped."
                    );
                if (actor.OwnerUid == binding.OwnerUid)
                    return;
            }
            if (!await OpenSidebarAsync(binding, ct))
                throw new InvalidOperationException("Account Menu could not be opened.");
            await _delay(TimeSpan.FromMilliseconds(500), ct);
            var loggedOut = false;
            for (var i = 0; i < 8; i++)
            {
                xml = await Screen(binding, ct);
                if (!MenuSurface(xml) || PFlowUiPrivacy.ContainsInput(xml))
                    throw new InvalidOperationException("Account Menu changed before logout.");
                if (FindControl(xml, "Log out") != null)
                {
                    loggedOut = await Tap(binding, ct, "Log out");
                    break;
                }
                var dimensions = ScreenTimeHierarchy
                    .Nodes(xml)
                    .Select(ScreenTimeHierarchy.Center)
                    .Where(p => p.HasValue)
                    .ToArray();
                if (dimensions.Length == 0)
                    break;
                var width = dimensions.Max(p => p!.Value.X) * 2;
                var height = dimensions.Max(p => p!.Value.Y);
                if (
                    !await ld.SwipeAsync(
                        binding.DeviceIndex,
                        width / 2,
                        height * 4 / 5,
                        width / 2,
                        height / 3,
                        400,
                        path,
                        ct
                    )
                )
                    break;
                await _delay(TimeSpan.FromMilliseconds(400), ct);
            }
            if (!loggedOut)
                throw new InvalidOperationException(
                    "Supported Log out control is missing; account was not rotated."
                );
            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }
        // Saved-login landing screens lead to the full form, never an ambiguous name-only login.
        var confirmedLogout = false;
        for (var i = 0; i < 15; i++)
        {
            xml = await Screen(binding, ct);
            var kind = PFlowLoginScreen.Classify(xml);
            if (kind == PFlowLoginScreenKind.LoginRequired)
                return;
            if (kind == PFlowLoginScreenKind.Checkpoint)
                throw new InvalidOperationException(PFlowLoginScreen.Message(kind));
            if (LogoutConfirmation(xml))
            {
                if (!allowLogout)
                    throw new InvalidOperationException(
                        "Logout confirmation requires account rotation permission."
                    );
                if (!confirmedLogout)
                {
                    if (!await Tap(binding, ct, "Log out"))
                        throw new InvalidOperationException("Logout confirmation is unavailable.");
                    confirmedLogout = true;
                }
                await _delay(TimeSpan.FromMilliseconds(400), ct);
                continue;
            }
            if (kind == PFlowLoginScreenKind.SaveLoginInfo)
            {
                if (!dismissedSaveInfo)
                {
                    if (!await Tap(binding, ct, "Not now"))
                        throw new InvalidOperationException(
                            "Save-login prompt could not be dismissed."
                        );
                    dismissedSaveInfo = true;
                }
                await _delay(TimeSpan.FromMilliseconds(400), ct);
                continue;
            }
            if (LoginLanding(xml))
            {
                if (
                    !await Tap(
                        binding,
                        ct,
                        "Log into another account",
                        "Log in to another account",
                        "I already have a profile",
                        "Use another profile"
                    )
                )
                    break;
            }
            await _delay(TimeSpan.FromMilliseconds(400), ct);
        }
        throw new InvalidOperationException(
            "Supported password login screen did not open; no credentials were entered."
        );
    }

    private static bool LogoutConfirmation(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && (
            Has(xml, "Log out of your account?")
            || Has(xml, "Log out of Facebook?")
            || Has(xml, "Are you sure you want to log out?")
        )
        && FindControl(xml, "Log out") != null;

    private async Task<bool> OpenSidebarAsync(PFlowAccountBinding binding, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var xml = await Screen(binding, ct);
            if (MenuSurface(xml))
                return true;
            if (
                PFlowUiPrivacy.ContainsInput(xml)
                || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                || PFlowAutoPostService.IsComposer(xml, "Reel")
            )
                return false;
            var menu = FindControl(xml, "Menu", "Account switcher, shortcuts, and settings menu");
            var tabs = ScreenTimeHierarchy
                .Nodes(xml)
                .Where(n =>
                    (string?)n.Attribute("package") == binding.AppPackage
                    && (string?)n.Attribute("enabled") == "true"
                    && (string?)n.Attribute("clickable") == "true"
                    && ((string?)n.Attribute("content-desc") ?? "").StartsWith(
                        "Menu, tab",
                        StringComparison.Ordinal
                    )
                )
                .Select(ScreenTimeHierarchy.Center)
                .Where(p => p.HasValue)
                .Distinct()
                .ToArray();
            menu ??= tabs.Length == 1 ? tabs[0] : null;
            if (menu != null)
            {
                if (!await ld.TapAsync(binding.DeviceIndex, menu.Value.X, menu.Value.Y, path, ct))
                    return false;
            }
            else if (FindControl(xml, "Back") != null)
            {
                if (!await ld.SendKeyAsync(binding.DeviceIndex, 4, path, ct))
                    return false;
            }
            else if (!await ld.OpenFacebookMenuFromFeedAsync(binding.DeviceIndex, path, ct))
                return false;
            await _delay(TimeSpan.FromMilliseconds(500), ct);
        }
        return false;
    }

    public static bool MenuSurface(string xml) =>
        !PFlowUiPrivacy.ContainsInput(xml)
        && !PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
        && !PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml)
        && (
            AtxAgentClient.IsMenuPageInBloks(AtxAgentClient.ParseBloksHierarchy(xml))
            || Has(xml, "Menu") && FindControl(xml, "Close menu") != null
            || SelectedMenuTab(xml)
            || (Has(xml, "Help and support") || Has(xml, "Help & support"))
                && (Has(xml, "Settings and privacy") || Has(xml, "Settings & privacy"))
                && (Has(xml, "Also from Meta") || Has(xml, "Upgrades"))
        );

    private static bool SelectedMenuTab(string xml) =>
        ScreenTimeHierarchy
            .Nodes(xml)
            .Any(n =>
                (string?)n.Attribute("package") == "com.facebook.katana"
                && (string?)n.Attribute("enabled") == "true"
                && (string?)n.Attribute("clickable") == "true"
                && (string?)n.Attribute("selected") == "true"
                && ((string?)n.Attribute("content-desc") ?? "").StartsWith(
                    "Menu, tab",
                    StringComparison.Ordinal
                )
                && ScreenTimeHierarchy.Center(n).HasValue
            );

    public Task<PFlowAccountObservation> LoginAsync(
        PFlowAccountBinding binding,
        PFlowCredential credential,
        Func<string, Task> progress,
        CancellationToken ct
    ) =>
        new PFlowNativeLoginNavigator(
            ld,
            path,
            ObserveAsync,
            new PFlowNativeOtpChallengeAdapter(
                ld,
                path,
                binding,
                selectAuthenticator: credential.TwoFactorKind == PFlowTwoFactorKind.TotpSecret
            ),
            new PFlowOtpCodeSource(
                receiveCode
                    ?? (
                        (_, _) =>
                            throw new InvalidOperationException(
                                "This login requires a verification code. Configure a TOTP secret or complete verification manually."
                            )
                    )
            )
        ).LoginAsync(binding, credential, progress, ct);
}
