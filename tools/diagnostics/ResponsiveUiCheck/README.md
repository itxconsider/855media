# Responsive UI check

Run `dotnet run --project tools/diagnostics/ResponsiveUiCheck` from the repository root.
Uses real Avalonia views with synthetic device rows and no production view model.
Does not open emulators, restore user queues or execute app commands.
Produces headless layout screenshots under `artifacts/responsive-ui`.
See `docs/responsive-ui-review.md` for coverage and limitations.
