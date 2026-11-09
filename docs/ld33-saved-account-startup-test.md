# LD-33 saved-account startup test

Follow-up: the reproduced startup failure is fixed and live recovery passed. See [saved-owner-switcher-recovery.md](saved-owner-switcher-recovery.md). The failing evidence below is retained as the before-fix result.

Live validation on 2026-11-04, LDPlayer9 installation `C:\LDPlayer\LDPlayer9`, index33. This tests the saved-owner preparation that the earlier successful current-account discovery runs did not exercise.

The diagnostic loaded the installation-scoped account and Page mappings without saving changes, verified the current instance binding, scanned the native actor list and selected Danny Stenhouse (Page). It then opened Menu, matching the previously captured failure surface: active Danny header, Rudolph White owner shortcut, and native switcher button.

It invoked the production `PFlowAutomaticSessionService.EnsureAsync` with `PFlowNativeAccountNavigator`, saved mappings and name-only verification, under the existing device lease. The planned subsequent Fetch supplied the saved Rudolph name and UID, rather than accepting any discovered owner. The diagnostic disables automatic login and rotation only on its in-memory account copy and uses a credential store that refuses all reads/writes/deletes. Pending-job, active-upload, input and authentication checks precede device actions.

## Result: failure reproduced

- Startup began at 00:10:41; name verification started at 00:10:43 and failed at 00:11:39, approximately58 seconds after startup began.
- Expected Rudolph White (Profile); observed Danny Stenhouse (type unresolved). Same exception as the prior desktop Fetch failure.
- Actual desktop Graph requests: **0**. Fetch and its optional API phase were never reached.
- Normal keyboard restoration passed. No publication, production mapping/options/credential/history-row changes or package.

Captured-frame replay identifies the rejection precisely. The account header reader recognizes Danny Stenhouse as Page and the Menu surface is recognized. The exact Rudolph owner shortcut exists and is itself enabled/clickable. Its non-clickable `android.view.ViewGroup` ancestor at `[0,48][632,1280]` is marked `enabled=false`, so `PersonalShortcut(requireUsable:true)` returns null. There is no active switcher, authentication, pending publication or input on the captured failure frame. The production guard is behaving as currently written; these observations do not authorize globally ignoring disabled ancestors. A scoped recovery/reader change remains necessary and is not implemented by this test.

## Restoration

A separate production native-discovery owner-return test found Danny Page, Rudolph Profile and Metsätalous Page, selected Rudolph, confirmed him again during final restoration and finished at00:13:22. Owner verification and normal-keyboard restoration passed with **0 Graph requests**. LD-33 was left on Rudolph's account.

## Evidence and reproduction

Artifacts: `artifacts/ld33-saved-startup/danny-menu` contains the staged Menu, failure/final XML and screenshots, timestamped log and `captured-owner-gates.txt`. `artifacts/ld33-saved-startup/restore-owner` contains restoration observations and captures.

```powershell
dotnet run --no-build --project tools/diagnostics/PageSwitchLiveCheck -- --ld=33 --folder=C:\LDPlayer\LDPlayer9 --launch --native-fetch --saved-account-startup "--start-page=Danny Stenhouse" --get-pages-by-api --links-only --output=artifacts/ld33-saved-startup/danny-menu
dotnet run --no-build --project tools/diagnostics/PageSwitchLiveCheck -- --inspect-fixture=artifacts/ld33-saved-startup/danny-menu/native-fetch-failure.xml "--inspect-owner=Rudolph White"
dotnet run --no-build --project tools/diagnostics/PageSwitchLiveCheck -- --ld=33 --folder=C:\LDPlayer\LDPlayer9 --native-fetch --return-owner-only --output=artifacts/ld33-saved-startup/restore-owner
```

Diagnostic build passed with zero warnings/errors. Production runtime code and main desktop output were not changed. This is validation and diagnosis, not a completed runtime fix; release counter remains42/10 toward3.2.5.
