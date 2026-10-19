# Automatic Page discovery

Fetch accounts now runs a cache-warming pass after personal-account verification, on initial fetch and mapped-account fetch. It checks the live personal UID, opens the native/Bloks-readable switcher, collects explicitly labelled Page rows while scrolling, visits full-name targets through existing native navigation, and compares live UID/owner with root records. It then restores the personal profile, merges the refreshed root snapshot for that owner, and runs the existing Page UI verification. Warming alone never grants verified Page access.

A repeated viewport ends the scan; 60 scrolls is the enumeration limit. Unknown or untyped layouts stop enrichment; truncated names are reported as unresolved. Existing cached records are retained. Page switches use existing bounded navigation and switching waits. Stop/error attempts personal-profile restoration with a separate 60-second deadline. The flow restores the verified personal profile, not an arbitrary initial Page.

Validation: 15 discovery/parser/switcher tests passed; desktop Debug build passed with zero warnings/errors. No live new-PC run was available. Facebook versions that omit explicit Page labels require a separate supported layout before automatic enumeration can proceed. No package produced; counted as improvement 4/10 toward 3.1.3.
