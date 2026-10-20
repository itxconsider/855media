# 855Media

Windows desktop application for media downloading, processing and Facebook/LDPlayer workflows. Module actions require activated subscription access.

## Project layout

| Path | Purpose |
| --- | --- |
| `855Media/` | Avalonia desktop UI, view models, services and bundled resources |
| `855Media.Core/` | Downloading, media processing, licensing and device automation |
| `855Media.Core.Tests/` | Automated Core regression tests |
| `android/885Helper/` | Android helper source and build script |
| `tools/` | Runtime helpers, license generator and developer diagnostics; see [tools guide](tools/README.md) |
| `docs/` | Release notes, feature notes and sanitized test fixtures |
| `dist/` | Ignored release packages; current package recorded in [AGENTS.md](AGENTS.md) |
| `artifacts/` | Ignored local checks, reports, previews and cleanup archives |

Build from the repository root:

```powershell
dotnet build 855Media.slnx
dotnet test 855Media.Core.Tests/855Media.Core.Tests.csproj
```

Known test limitations are recorded in the current release notes. Diagnostic tools that operate an emulator are manual tools; building them does not run them.

Local Python environments, models, voice-training assets, licenses, signing keys and browser cookies are local data. Keep them separate from source control and release archives. Do not remove them as generic cleanup targets.
