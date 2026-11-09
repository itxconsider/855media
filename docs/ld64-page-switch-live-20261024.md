# LD-64 live Page switch test — 2026-10-24

User-authorized navigation-only test to the one mapped Page, Mattea Page+, using the current production `LDPlayerService.SwitchToPersonaAsync` and Graph-bound identity components. The diagnostic acquired the device lease, checked pending publication history and active upload notifications, and confirmed Android was running before navigating. No post/composer action, mapping edit or app restart occurred.

Result: **failed after 22.1 seconds** with `Mapped target could not be selected. No work started.` The switcher was recognized as `switcher.footer.v1`, but the native target-row selector rejected its layout and never selected Mattea Page+. This reproduces the queued-job failures seen earlier; the Page is visible in the recorded switcher, so this result does not establish lost/deleted Page access.

The initial personal actor, Альона Лисенко, remained active and was freshly confirmed through Graph afterward. Two Graph reads total: initial identity and final unchanged-actor check. No restoration switch was needed. The Facebook UI remained on its switcher.

Evidence: `artifacts/ld64-page-switch-live/results.json`, start/failed/final screenshots and redacted XML, `artifacts/ld64-switch-run.log`, and the session trace at approximately 01:07:34–01:07:56 Bangkok time.

The diagnostic now accepts explicit `--ld` and `--expected-pages` arguments (defaults preserve LD-56/seven Pages). Invocation: `dotnet run --project tools/diagnostics/PageSwitchLiveCheck -c Release -- --ld=64 --expected-pages=1 --run --first-only`. It avoids a redundant restoration switch when fresh Graph shows the starting actor is unchanged. This is diagnostic support and live validation only; no production fix or counter credit. Counter stays 8/10; packages unchanged.
