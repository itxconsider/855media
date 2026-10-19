# Settings preview

Run `dotnet run --project tools/SettingsPreview/SettingsPreview.csproj -c Debug` from the repository root.

Renders the real Settings view with the application's theme in headless Avalonia.
Uses isolated temporary settings and a rendering-only license stub; does not load
user accounts, restore queues, contact an activation server, or open emulators.
The stub cannot activate a license and is not included in the application build.

Writes inactive, monthly, lifetime, and seven category screenshots under
`artifacts/`, and verifies the duration labels. Nonzero exit means preview failure.
