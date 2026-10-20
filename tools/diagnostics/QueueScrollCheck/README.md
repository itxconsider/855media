# Queue scrolling regression check

Run `dotnet run --project tools/diagnostics/QueueScrollCheck/QueueScrollCheck.csproj -c Debug`.

This headless Avalonia check uses the production `PostQueueGrid` control and the
same Material DataGrid theme as the desktop app. It loads a 36-row queue and
verifies bounded normal/accelerated wheel movement, access to the middle and end,
and viewport stability during status updates, selection, appends, and sorting.
It also checks narrow and resized windows with both scrollbars visible, bounded
horizontal wheel movement, and stable vertical position during horizontal scrolling.
Failures return a nonzero exit code. It does not launch the desktop app or access
accounts, emulators, real queues, or posting services.
