# PFlow live login check

Run from the repository root. The requested device must have exactly one mapped
FB-01 account. Logout/login requires a readable protected credential record;
inspection and finishing UID verification do not read credentials.

```powershell
dotnet run --project tools/diagnostics/PFlowLoginCheck/PFlowLoginCheck.csproj -- --inspect 1 C:\LDPlayer\LDPlayer9
dotnet run --project tools/diagnostics/PFlowLoginCheck/PFlowLoginCheck.csproj -- --logout-login 1 C:\LDPlayer\LDPlayer9 --live
dotnet run --project tools/diagnostics/PFlowLoginCheck/PFlowLoginCheck.csproj -- --recover-login 1 C:\LDPlayer\LDPlayer9 --live
```

`--inspect` proves the current personal UID through its own profile settings.
`--snapshot` writes a redacted diagnostic hierarchy. `--logout-login --live`
exercises sidebar logout and password recovery through the production adapters.
It continues an interrupted logout when a supported pending dialog/landing is
already visible. If the password form is already open, the report explicitly
records `StartedLoggedOut=true`.

`--finish-login <index> <LD folder> --live` dismisses a save-login prompt or checks
an authenticated account after an interrupted login. It never resubmits a password
or code and stops if authentication is still required.

`--recover-login <index> <LD folder> --live` runs the automatic session service
without forcing logout. It reuses an already verified personal session or recovers
the supported password form once using protected credentials.

The tool verifies the exact LD/app/UID binding, holds the device lease, and checks
unresolved publication/ScreenTime history and ongoing uploads. It does not
instantiate the publisher or ScreenTime runner. Stored selections and automatic
login/rotation flags are not saved. Android saved-password prompts are closed;
the password comes only from the protected 855Media credential store.

Reports are under `bin/Debug/net10.0/reports/`. Only redacted XML and non-secret
result metadata are saved. No authentication screenshots or credential values are
printed. A result is successful only after a fresh exact personal UID check.
