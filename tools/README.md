# Tools

Run commands from the repository root. Runtime helper paths are stable because application code and publishing rules use them.

## Runtime and support tools

| Path | Purpose |
| --- | --- |
| `855Media.KeyGen/` | RSA-signed machine-bound license generator; retain signing keys outside releases |
| `atx/` | Android UI agent, keyboard helper and setup/inspector scripts |
| `885helper/` | Built Android helper distributed with the app |
| `ffmpeg/` | FFprobe and dependency licensing |
| `yt-dlp.exe` | Downloader companion executable |
| `whisper/` | Local speech binaries/models, ignored by Git |
| `rvc/` | Independent voice-conversion inference script and checks |
| `meta_dubbing/` | Dubbing worker and its tests |
| Root Python/batch scripts | Speech preparation, separation, dataset cleaning and VoxCPM runtime entry points |

## Developer diagnostics

Developer diagnostic directories are under `tools/diagnostics/`. They are not application runtime dependencies. Their names distinguish offline checks/previews from live device tools; retain historical read-only and navigation checks for regression investigation.

Examples that do not operate an emulator:

```powershell
node tools/diagnostics/FacebookPhotoCollectorCheck/check.cjs
node tools/diagnostics/FacebookPhotoCollectorCheck/detail-check.cjs
dotnet run --project tools/diagnostics/FacebookPhotoCollectorCheck/BrowserCheck/BrowserCheck.csproj
dotnet run --project tools/diagnostics/DownloadLifecycleCheck/DownloadLifecycleCheck.csproj
dotnet run --project tools/diagnostics/DownloadFilterCheck/DownloadFilterCheck.csproj
dotnet run --project tools/diagnostics/ReelLayoutLiveCheck/ReelLayoutLiveCheck.csproj -- --write-catalog
```

The BrowserCheck covers Facebook photo captions/full-size resolution and Instagram browser collection/photo/carousel/Reel resolution using locally intercepted fixture requests in an isolated WebView profile. ResponsiveUiCheck also verifies browser-mode switching, Instagram toolbar layout and inactive subscription guards. The catalog command writes the bundled layout JSON and exits before device operations. Other live-tool commands can navigate devices or publish; inspect their source/README and select the intended device before running them. They are never run as part of project cleanup.

The removed Real-ESRGAN tool is archived outside the active source tree; it is not a supported runtime dependency. See [cleanup record](../docs/project-cleanup.md).

Account management table offline validation (synthetic data; no LD operations):

```powershell
dotnet run --project tools/diagnostics/AccountTableCheck/AccountTableCheck.csproj
```

Checks actual account search/filter/numeric order, hidden selected drafts and rendered DataGrid rows/actions at 800/1280/1800 pixels. Previews are written to `artifacts/accounts-table`.
