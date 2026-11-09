# Native Fetch naming (2026-11-02)

Replaced RootFree naming with Native in Fetch runtime types, interfaces, scope guards, desktop methods, test classes, diagnostic reflection targets and source filenames. The coordinator is PFlowNativeFetch, device interface IPFlowNativeFetchDevice, adapter PFlowNativeFetchDevice, scope PFlowNativeFetchScope and desktop route ProcessNativeFetchAsync. Device tests use PFlowNativeFetchDeviceTests. AccountTableCheck uses NativeFetchCheck and --native-fetch. Documentation references and the former root-free-fetch.md filename now use native-fetch.md.

This is a naming refactor; protected-session rules and Fetch behavior are retained. Historical generated artifacts and release archives were not rewritten.

Validation: 113 focused Core checks passed; actual isolated desktop progressive checkpoint plus 17 Fetch scenarios passed through the renamed reflection route. Main Debug and PageSwitchLiveCheck builds succeeded with zero warnings/errors. Main build followed fresh app-absence verification. Source scan found no RootFree/root-free references in runtime/tests/diagnostics and no matching source/document filenames. No live device operation, production data write, publication or package.
