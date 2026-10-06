# OpenATX Ecosystem Integration for 855Media

This folder contains utilities, setup scripts, and binaries for integrating **OpenATX** across all LDPlayer automation and Facebook scraping functions in 855Media.

---

## 1. Quick Tools

### A. Element Inspector (`start_inspector.bat`)
Double-click `start_inspector.bat` or run:
```bash
npx uiautodev -open
```
- Opens a browser-based UI inspector connected to your running LDPlayer instances.
- Allows you to click any element on the Facebook screen to immediately view its Resource ID, Text, Class, XPath, and exact Pixel Bounds `[x1,y1][x2,y2]`.

### B. Deploy `atx-agent` Daemon (`setup_atx_agent.ps1`)
Run in PowerShell:
```powershell
.\setup_atx_agent.ps1
```
- Automatically downloads and pushes the `atx-agent` binary to `/data/local/tmp/atx-agent` on all active emulators.
- Forwards ports `7912` (LD-0), `7913` (LD-1), `7914` (LD-2), etc.
- Enables single-digit millisecond UI hierarchy dumps, clicks, shell execution, and HTTP streaming uploads in 855Media.

---

## 2. OpenATX Applied to All Functions

All automation across 855Media now funnels through OpenATX first, with transparent fallback to ADB:

| Function Category | Method in `LDPlayerService` | OpenATX HTTP Implementation | Fallback |
| :--- | :--- | :--- | :--- |
| **File Push / Upload** | `PushFileToEmulatorAsync` | `POST /upload/{path}` multipart streaming | `ldconsole adb push` |
| **File Download** | `DownloadFileFromEmulatorAsync` | `GET /raw/{path}` direct byte stream | `ldconsole adb pull` |
| **UI Hierarchy Dump** | `DumpUiHierarchyAsync` | `GET /dump/hierarchy` JSON-RPC (~15ms) | `uiautomator dump` + `cat` |
| **UI Element Tap** | `TapAsync`, `ClickElementByTextAsync`, `ClickElementByResourceIdAsync`, `ClickElementByDescAsync` | Fast node bounds resolution + `input tap {cx} {cy}` | XML search + ADB tap |
| **UI Text Input** | `InputTextAsync` | Smart routing: Unicode/multiline via Base64 broadcast, ASCII via `/shell` | ADB shell `input text` |
| **ADB Keyboard** | `InputTextAdbKeyboardAsync`, `ClearTextAdbKeyboardAsync`, `SendAdbKeyboardEditorActionAsync`, `InstallAdbKeyboardAsync` | `am broadcast -a ADB_INPUT_B64 --es msg {b64}` (100% Unicode, Khmer, Emojis, linebreaks) | ADB broadcast |
| **Navigation Keys** | `PressHomeAsync` (3), `PressBackAsync` (4), `PressRecentAppsAsync` (187), `WakeUpAsync` (224) | `input keyevent {code}` via OpenATX `/shell` | ADB shell `input keyevent` |
| **App Lifecycle** | `LaunchAppAsync`, `StopFacebookAppAsync`, `ClearFacebookCacheAsync` | `StartAppAsync`, `StopAppAsync`, `ClearAppCacheAsync` via `/shell` | `runapp`, `am force-stop`, `rm -rf` |
| **Deep Linking** | `OpenDeepLinkAsync` | `am start -a android.intent.action.VIEW -d ...` via OpenATX | ADB shell `am start` |
| **Media Operations** | `ScanMediaFileAsync`, `ShareVideoToFacebookAsync` | `MEDIA_SCANNER_SCAN_FILE` & `ACTION_SEND` broadcast/intent via OpenATX | ADB shell broadcast |
| **Device Info** | `GetAtxDeviceInfoAsync` | `GET /info` (model, brand, display resolution, battery, memory, sdk) | `getprop` calls |
| **Process Status** | `IsAppRunningAsync` | `GET /pidof/{pkgname}` (single-ms PID check) | ADB `pidof` / `ps` |
| **Screenshots** | `TakeScreenshotAsync` | `GET /screenshot` (direct frame buffer JPEG/PNG) | `screencap` + `pull` |
| **Facebook Scraper** | `ExtractFacebookAccountDetailsAsync`, `FetchDeviceFacebookAccountAndPagesAsync`, `FetchPageInfoFromDeviceAsync` | End-to-end OpenATX hierarchy dumps, switcher dismissal, SQLite/prefs extraction | ADB |
| **Bloks Native Shell** | `OpenBloksAccountsCenterAsync`, `IsBloksNativeShellActiveAsync`, `DumpBloksScreenAsync`, `ClickBloksElementAsync`, `ExtractBloksAccountInfoAsync` | Meta Bloks server-driven UI deep linking, bounds resolution, field extraction | ADB `am start` + fallback |

---

## 3. Meta Bloks Native Shell Automation

Facebook Katana renders modern screens (Meta Accounts Center, Switch Profile, Page Creation, Settings, Personal Details, Reels tools) using **Bloks Native Shell** (`BloksNativeShellActivity` / `BloksHostedActivity`).

Because Bloks omits conventional Android `resource-id`s, `AtxAgentClient` and `LDPlayerService` provide dedicated Bloks support:

- **Launch by Bloks App ID or Deep Link**:
  ```csharp
  await atxClient.OpenBloksAccountsCenterAsync(deviceIndex);
  await atxClient.OpenBloksNativeShellAsync(deviceIndex, "com.bloks.www.fx.settings.modular.screen");
  ```
- **State & Focus Detection**:
  ```csharp
  bool isBloks = await atxClient.IsBloksNativeShellActiveAsync(deviceIndex);
  string? focus = await atxClient.GetCurrentFocusedActivityAsync(deviceIndex);
  ```
- **Bloks Element Targeting & Scrolling**:
  ```csharp
  await atxClient.ClickBloksElementAsync(deviceIndex, "Personal details");
  await atxClient.ScrollAndClickBloksElementAsync(deviceIndex, "Switch profile");
  ```
- **Account & Field Data Extraction**:
  ```csharp
  string? email = await atxClient.ExtractBloksFieldValueAsync(deviceIndex, "Contact info");
  string? bday = await atxClient.ExtractBloksFieldValueAsync(deviceIndex, "Birthday");
  BloksFacebookAccountInfo account = await atxClient.ExtractBloksAccountInfoAsync(deviceIndex);
  ```

---

## 4. ADB Keyboard Integration

Standard Android `input text` keystrokes frequently drop letters, break on quotes/spaces/newlines, and completely fail on non-ASCII text (Khmer, Thai, Chinese, Cyrillic, Emojis).

`855Media` integrates **ADBKeyboard** (`com.android.adbkeyboard/.AdbIME`) using Base64 broadcast delivery (`ADB_INPUT_B64`):

- **Automatic Lossless Routing**:
  Calling `ldPlayerService.InputTextAsync(index, text)` automatically routes any string with emojis, Unicode, quotes, or newlines through `ADB_INPUT_B64`.
- **Direct Base64 Text Input**:
  ```csharp
  // Fast, instant, 100% UTF-8 lossless text entry:
  await ldPlayerService.InputTextAdbKeyboardAsync(0, "សួស្តី Reel Caption 🔥🎬\n#viral #trending", clearFirst: true);
  ```
- **Clear Active Input Field**:
  ```csharp
  await ldPlayerService.ClearTextAdbKeyboardAsync(0);
  ```
- **Editor Actions (Search, Done, Go, Send)**:
  ```csharp
  await ldPlayerService.SendAdbKeyboardEditorActionAsync(0, AdbEditorAction.Search);
  await ldPlayerService.SendAdbKeyboardEditorActionAsync(0, AdbEditorAction.Done);
  ```
- **Automated Deployment**:
  `setup_atx_agent.ps1` automatically downloads `ADBKeyboard.apk` into `tools/atx/bin/` and enables it as the active IME on all running emulators. Alternatively, call `ldPlayerService.InstallAdbKeyboardAsync(index)`.


