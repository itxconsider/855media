# LD-23 stalls with its Reel album visible

The 2026-10-21 00:05 +07:00 log for job 18206b3d3f0148789970a5d35bf78798 passed identity and media transfer, then reported Native Reel layout unavailable: StoryAlbums. No Share was requested for that job; subsequent cleanup verified the job file, album and media index removed.

Saved UI: p-flow-ui/20261020-170546793-LD-23-native-reel-unavailable.{xml,png}. Its hierarchy is retained at docs/fixtures/pflow-layouts/ld23-reel-albums-recycler.xml.

The captured Facebook variant has an androidx.recyclerview.widget.RecyclerView. Its clickable android.view.ViewGroup album rows have accessibility labels such as Reel_18206b3d3f0148789970a5d35bf78798, 1, Media, with a child label containing the full album name. Previous recognition required an android.widget.ListView containing android.widget.Button rows named Select album, ... . The capture also marks a passive ancestor ViewGroup disabled while the RecyclerView and row controls remain enabled.

The recognizer now accepts the observed Reel-specific row structure only alongside Create reel and the album spinner. All clickable rows in a candidate list must match the album-row format and child names; ambiguous lists are rejected. Selection requires the full exact album name. Existing old-style album buttons are preserved. Only matching album spinners and Media row controls receive the existing read-only RecyclerView container exception; disabled/hidden actual controls still fail. Scrolling uses the identified list only when Facebook marks it scrollable; the captured three-row list does not require scrolling.

Tests cover the captured hierarchy, exact versus partial/wrong job IDs, mixed Gallery rejection, disabled rows, foreign apps, scrollable versus non-scrollable lists, and a complete native Reel dry-run through permissions, the captured picker, the captured RecyclerView album list and exact job-media selection without publishing. Test log: artifacts/ld23-recycler-albums-tests.log. Build log: artifacts/ld23-albums-build.log. The test build is in artifacts/repo-popup-picker-check; the active app is not restarted or patched in place.
