

namespace _855Media.Core.LDPlayer;

/// <summary>
/// IME Editor Actions supported by ADBKeyboard (ADB_EDITOR_CODE).
/// </summary>
public enum AdbEditorAction
{
    Unspecified = 0,
    None = 1,
    Go = 2,
    Search = 3,
    Send = 4,
    Next = 5,
    Done = 6,
    Previous = 7,
}

/// <summary>
/// Constants for ADBKeyboard APK, intents, broadcast actions, and component names.
/// </summary>
public static class AdbKeyboardConstants
{
    public const string PackageName = "com.android.adbkeyboard";
    public const string ImeComponent = "com.android.adbkeyboard/.AdbIME";

    public const string ActionInputText = "ADB_INPUT_TEXT";
    public const string ActionInputB64 = "ADB_INPUT_B64";
    public const string ActionClearText = "ADB_CLEAR_TEXT";
    public const string ActionInputCode = "ADB_INPUT_CODE";
    public const string ActionEditorCode = "ADB_EDITOR_CODE";

    public const string OfficialApkDownloadUrl =
        "https://raw.githubusercontent.com/senzhk/ADBKeyBoard/master/ADBKeyboard.apk";
    public const string OfficialFallbackApkDownloadUrl =
        "https://github.com/senzhk/ADBKeyBoard/raw/master/ADBKeyboard.apk";
}
