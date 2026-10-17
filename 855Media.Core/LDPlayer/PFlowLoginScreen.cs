using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

public enum PFlowLoginScreenKind
{
    Unsupported,
    LoginRequired,
    CredentialsRejected,
    DeviceApprovalRequired,
    VerificationMethods,
    AuthenticatorCodeRequired,
    SaveLoginInfo,
    OwnPersonalProfile,
    Checkpoint,
    LocationServicesIntro,
    ContactsAccessIntro,
    LanguageSuggestion,
}

// Observed installed-screen markers. A profile marker is NOT exact live UID proof.
public static class PFlowLoginScreen
{
    // False only means no recognized authentication prompt; it does not establish identity.
    public static bool RequiresAuthentication(PFlowLoginScreenKind kind) =>
        kind
            is PFlowLoginScreenKind.LoginRequired
                or PFlowLoginScreenKind.CredentialsRejected
                or PFlowLoginScreenKind.DeviceApprovalRequired
                or PFlowLoginScreenKind.VerificationMethods
                or PFlowLoginScreenKind.AuthenticatorCodeRequired
                or PFlowLoginScreenKind.Checkpoint
                or PFlowLoginScreenKind.SaveLoginInfo;

    public static PFlowLoginScreenKind Classify(
        string? xml,
        string package = "com.facebook.katana"
    ) => FacebookHierarchyCache.Memo(xml, "login:" + package, () => ClassifyCore(xml, package));

    private static PFlowLoginScreenKind ClassifyCore(string? xml, string package)
    {
        if (package != "com.facebook.katana")
            return PFlowLoginScreenKind.Unsupported;
        try
        {
            var nodes = FacebookHierarchyCache
                .ParseStrict(xml ?? "")
                .Descendants("node")
                .Where(n => (string?)n.Attribute("package") == package)
                .ToArray();
            bool Has(string text) =>
                nodes.Any(n =>
                    (string?)n.Attribute("text") == text
                    || (string?)n.Attribute("content-desc") == text
                );
            bool Button(string label, bool requireEnabled = true) =>
                nodes.Any(n =>
                    (!requireEnabled || (string?)n.Attribute("enabled") == "true")
                    && (string?)n.Attribute("clickable") == "true"
                    && (
                        string.Equals(
                            (string?)n.Attribute("text"),
                            label,
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            (string?)n.Attribute("content-desc"),
                            label,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                );
            if (
                nodes.Any(n =>
                    (
                        (string?)n.Attribute("text") ?? (string?)n.Attribute("content-desc") ?? ""
                    ).EndsWith(
                        ", confirm you're human to use your account",
                        StringComparison.Ordinal
                    )
                )
                && Button("Get support")
                && Button("Continue")
            )
                return PFlowLoginScreenKind.Checkpoint;
            if (Has("Wrong Credentials") && Has("Invalid username or password"))
                return PFlowLoginScreenKind.CredentialsRejected;
            if (
                Has("Check your notifications on another device")
                && Has("Waiting for approval")
                && Button("Try another way")
            )
                return PFlowLoginScreenKind.DeviceApprovalRequired;
            if (
                Has("Choose a way to confirm it’s you")
                && Button("Authentication app, Get a code from your authentication app.")
                && Button("Continue")
            )
                return PFlowLoginScreenKind.VerificationMethods;
            if (
                Has("Go to your authentication app")
                && Button("Continue", false)
                && nodes.Count(n =>
                    (string?)n.Attribute("class") == "android.widget.EditText"
                    && ((string?)n.Attribute("content-desc"))?.Split(',')[0] == "Code"
                    && (string?)n.Attribute("enabled") == "true"
                ) == 1
            )
                return PFlowLoginScreenKind.AuthenticatorCodeRequired;
            if (Has("Save your login info?") && Button("Not now"))
                return PFlowLoginScreenKind.SaveLoginInfo;
            // These are optional onboarding screens, not proof of an active UID.
            // Normalize wrapped text and Chinese full-width parentheses only for
            // these screenshot-derived labels; keep login/challenge rules exact.
            string PromptText(string? value) =>
                Regex.Replace(value ?? "", @"\s+", "").Replace('（', '(').Replace('）', ')');
            bool PromptHas(string label) =>
                nodes.Any(n =>
                    !n.AncestorsAndSelf("node")
                        .Any(a => (string?)a.Attribute("visible-to-user") == "false")
                    && (
                        PromptText((string?)n.Attribute("text")) == PromptText(label)
                        || PromptText((string?)n.Attribute("content-desc")) == PromptText(label)
                    )
                );
            bool PromptSurface() =>
                !PFlowUiPrivacy.ContainsInput(xml)
                && !FacebookHierarchyCache
                    .ParseStrict(xml ?? "")
                    .Descendants("node")
                    .Any(n =>
                        (string?)n.Attribute("package") is { Length: > 0 } owner
                        && owner != package
                        && owner != "com.android.systemui"
                    );
            if (
                PromptHas("Allow contacts access to find people to follow")
                && PromptHas(
                    "See who's also on Facebook by uploading your contacts and then choosing which friends to add."
                )
                && PromptHas("Skip")
                && PromptHas("Continue")
                && PromptSurface()
            )
                return PFlowLoginScreenKind.ContactsAccessIntro;
            if (
                PromptHas("设置语言")
                && PromptHas("试用中文(简体)版 Facebook")
                && PromptHas("Continue in English (US)")
                && PromptSurface()
            )
                return PFlowLoginScreenKind.LanguageSuggestion;
            if (
                !PFlowUiPrivacy.ContainsInput(xml)
                && Has("To use Location Services, allow Facebook to access your location")
                && Has("How you can use Location Services")
                && Button("Continue")
                && Button("Skip")
            )
                return PFlowLoginScreenKind.LocationServicesIntro;
            if (
                Button("Edit cover photo button")
                && Button("Edit profile picture")
                && Button("Edit profile")
            )
                return PFlowLoginScreenKind.OwnPersonalProfile;
            if (
                PFlowNativeLoginNavigator.Control(xml, "login") != null
                && PFlowNativeLoginNavigator.Control(xml, "password") != null
                && PFlowNativeLoginNavigator.Control(xml, "Log in") != null
            )
                return PFlowLoginScreenKind.LoginRequired;
        }
        catch (System.Xml.XmlException) { }
        return PFlowLoginScreenKind.Unsupported;
    }

    public static string Message(PFlowLoginScreenKind kind) =>
        kind switch
        {
            PFlowLoginScreenKind.LoginRequired =>
                "Login required for this account on its assigned LD.",
            PFlowLoginScreenKind.CredentialsRejected =>
                "Facebook rejected login. Check credentials and the current network before retrying.",
            PFlowLoginScreenKind.DeviceApprovalRequired =>
                "Approve this login on your existing device or use an offered verification method.",
            PFlowLoginScreenKind.VerificationMethods =>
                "Choose an available verification method for this account.",
            PFlowLoginScreenKind.AuthenticatorCodeRequired =>
                "Enter this account’s current authenticator code.",
            PFlowLoginScreenKind.SaveLoginInfo =>
                "Facebook is asking whether to save login information.",
            PFlowLoginScreenKind.OwnPersonalProfile =>
                "Personal profile visible; verify its exact UID before continuing.",
            PFlowLoginScreenKind.Checkpoint =>
                "Facebook requires a human verification checkpoint. Complete it manually in this LD; automation is paused.",
            PFlowLoginScreenKind.ContactsAccessIntro =>
                "Facebook is offering contact uploading; Skip is available.",
            PFlowLoginScreenKind.LanguageSuggestion =>
                "Facebook is suggesting Simplified Chinese; Continue in English (US) is available.",
            _ =>
                "Facebook screen is unsupported. Review the assigned LD; no guessed action was made.",
        };
}
