# FFprobe fresh-PC setup

Future Windows packages include tools/ffmpeg/ffprobe.exe, LICENSE.txt and README.txt from Gyan FFmpeg 8.1 essentials (https://github.com/GyanD/codexffmpeg/releases/tag/8.1). Publish rejects a missing executable. Existing 3.1.2 outputs are unchanged.

Downloader initialization now checks FFprobe separately from FFmpeg, including when FFmpeg is already present or custom configured. It uses an existing custom FFmpeg sibling first, then existing normal probe paths. Missing Windows x64 probes download the pinned 8.1 archive and install only bin/ffprobe.exe into %LOCALAPPDATA%/855Media/tools/ffmpeg. The locator includes that directory. Setup serializes concurrent requests, rejects missing/empty/ambiguous archive entries, stages the executable before replacement, cleans temporary files, and displays setup failures through the snackbar. Automatic fallback requires internet access. Other architectures require an existing compatible probe.

Validation: 4 archive extraction tests passed (valid, missing, empty, duplicate), Debug desktop build passed, self-contained Windows Release publish passed to artifacts/ffprobe-publish-check, published binary SHA-256 matched source, and both binaries ran ffprobe -version successfully. A clean Windows machine was not operated. No beta was repackaged; improvement 7/10 toward 3.1.3.
