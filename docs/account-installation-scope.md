# LD-Accounts installation scope

Refresh mappings previously rebuilt the account table from every saved account,
regardless of the selected LDPlayer folder. Identical index numbers made this
especially confusing across LDPlayer 9 and 14.

The default table now includes only saved instance identities matching the
selected installation's current config and disk identity. The heading shows the
actual configured folder. Refresh mappings rescans devices before rebuilding the
table; changing folders clears stale validity and bulk selection immediately.
The selected management device is also replaced with an item from the new scan.

The All saved installations filter retains access to unmatched records. Such
records cannot be reported as mapped to the current installation. Existing hashed
bindings cannot distinguish an unrelated installation from a missing or replaced
instance; the diagnostic states both possibilities. No account is automatically
transferred, deleted or rebound. Saving account options excludes identities outside
the current scope. The configured folder remains authoritative; opening an LD14
window does not silently change a configured LD9 installation.

Validation: nine Core installation/port/lease checks passed, including three new
scope cases for identical indices/names, unreadable/missing configs and renamed
instances. Ten isolated desktop checks passed through the actual ViewModel:
default scope, active-folder heading, visible-only bulk selection, all-saved review,
option retention, search isolation, folder-change invalidation and scope reload.
Main Debug 3.2.4 built with zero warnings/errors after fresh app-absence checks.
No live emulator operation, production mapping/config edit or package.
