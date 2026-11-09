# Data-driven selection and Albums controls

Completed 2026-11-03.

## Registered selection evidence

`switcher.selection-confirmation.v1` is a context-specific `SwitcherSelection` component exposing the `SelectedActor` control. It requires an explicit expected name and a fresh, unique, fully visible, enabled selected native row. Exact name matching strips notification suffixes through the shared matcher. The component is excluded from unscoped whole-screen classification to prevent overlap with the ordinary switcher screen variants.

Fetch consumes this registry result after checking invocation-local actor membership and positive Page-badge contradictions. The component proves a selected **name**; it does not invent the Profile/Page type. Existing complete-list, known-type, process, foreground and binding checks remain at their current boundaries.

The JSON catalog can version, disable, exclude labels from, or add label requirements to this component. C# structural guards are inherited. Identity-control aliases remain forbidden; configurations cannot rename Edit profile into SelectedActor or weaken selection proof. Conflicting variants with the same base/version are rejected during load, retaining the prior valid catalog.

## Registered Reel navigation

`reel.picker-tabbed.v1` now exposes an `Albums` control resolved from the actual tab strip. Native Reel navigation resolves this control through the registry on a fresh hierarchy before tapping its semantic node. Missing/disabled/duplicate/foreign controls and disabled JSON variants do not authorize the tap. The reveal gesture and exact generated-album/file checks remain unchanged. Screenshot reconstructions without an actionable tab can still identify the picker while reporting the control unavailable.

## Configuration and compatibility

The user configuration remains `%APPDATA%/855Media/layouts/facebook-layouts.json`, formatVersion1. Existing files inherit newly shipped omitted templates; this change does not overwrite custom files. The bundled default was regenerated from the compiled catalog, including previously omitted shipped photo templates. An example additional variant is:

```json
{
  "id": "switcher.selection-confirmation.v2",
  "baseId": "switcher.selection-confirmation.v1",
  "version": 2,
  "enabled": true,
  "requiredLabels": [],
  "excludedLabels": ["Confirm your identity"],
  "controlAliases": {}
}
```

Add variants within the existing root `variants` array and use the existing layout reload flow. Higher matching versions supersede older versions; a nonmatching stricter variant leaves matching lower-version rules available. Disable the base as well if the intention is to prohibit that fallback.

## Validation

All236 focused tests passed across JSON/catalog parity, registry/component matching, Fetch selection and discovery, Reel navigation and recent-app handling. Five new tests cover older-file inheritance, exact selected-name requirements, version/disable/required-label rules, forbidden aliases, unsafe/disabled controls, conflicting versions, the real scrolled Albums tab, and actual Fetch behavior when the configured selection component is disabled. Captured fixtures and actual simulated runners verify the consumers, not just the catalog metadata.

Main Debug and catalog-export diagnostic builds succeeded with zero warnings/errors after main app-absence verification. The catalog export required a fresh diagnostic restore to replace stale dependency metadata; no project/version source was changed for that repair. Executable: `855Media/bin/Debug/net10.0/855Media.exe`.

No live device navigation, production config/data changes, publication or release package was performed for this registry change. Previous live return-to-owner results are documented separately; no new live claim is made. LD9's old recent-app flow and LD14's new flow are unchanged.
