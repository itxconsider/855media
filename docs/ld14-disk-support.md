# LD14 disk compatibility and Fetch status wording

The October 29 LD14-45 Fetch read the account and Page details, then failed before
persistence because instance identity lookup required data.vmdk. That installation
stores its data in data.vdi.

The shared instance identity reader now accepts exactly one existing data.vmdk or
data.vdi. Its hashing contract is unchanged for existing VMDK instances. Missing
disks, missing Android identity and two competing data disks still stop the flow;
disk creation time continues to detect replacement. This also enables current
installation account-table matching for VDI instances without changing bindings.

Fetch progress and completion messages omit implementation terms such as root,
Graph, native UI and UID source. Account and single-Page completion use plain
wording. Reused saved details remain explicitly described as saved details rather
than being presented as freshly obtained identifiers. Technical method names,
trace event keys and diagnostic-tool output remain available for troubleshooting.

Validation:

- 75 focused Core checks passed, including five disk-format cases and a new
  progress-message case. Initial run retained an outdated wording assertion; its
  replacement checks the same saved-detail disclosure in plain language.
- 15 isolated actual desktop Fetch scenarios passed, including new VDI current-
  account and single-Page persistence paths. Successful statuses are checked for
  technical-term leakage; existing source, sibling, removal and pending-post
  safeguards remain covered.
- Read-only local identity checks succeeded for real LD14 indices 6 and 45.
  No emulator navigation or live Fetch was performed for this validation.
- Main Debug 3.2.4 built with zero warnings/errors after fresh app-absence checks.

No production mapping/config edit, publication or package. Full live Fetch retry
remains pending. Evidence: artifacts/ld14-disk-support.
