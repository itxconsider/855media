# Download cancellation lifecycle

Removing a download row disposed its CancellationTokenSource while EnqueueDownload
was still running. The catch block then read DownloadViewModel.CancellationToken,
which accessed the disposed source's Token property. This threw a second exception
out of the async-void handler and reached the Avalonia dispatcher.

DownloadViewModel now caches the token value while its source is alive. Disposal
cancels active work before disposing the source. Reset/retry cannot revive a
disposed row. Manual retry cancels the old attempt and creates a fresh token,
including when the previous attempt was waiting for automatic retry.

EnqueueDownload captures one token for its entire attempt, including exception
classification and retry delays. It rejects disposed rows and checks cancellation
and row membership before automatically re-enqueueing. Removing a row detaches
its retry event handler.

Validation:

- `tools/diagnostics/DownloadLifecycleCheck` runs twelve checks against the actual desktop view
  model, including the reported post-disposal token read, repeated disposal,
  cancellation of pending work, manual retry and prevention of disposed-row revival.
  No downloads, device actions, user settings writes or subscription bypasses run.
- 149 focused downloading tests passed.
- Isolated desktop build passed with zero warnings/errors.

Run the view-model checks without replacing the running application:

```powershell
dotnet run --project tools/diagnostics/DownloadLifecycleCheck/DownloadLifecycleCheck.csproj -p:OutputPath=D:/repos/855Media/artifacts/download-lifecycle-check/
```

Source/test build only; the running app and existing release package were not replaced.
