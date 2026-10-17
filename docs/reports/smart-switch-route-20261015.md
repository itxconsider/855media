# Switching from the current screen

Mapped Page/profile switching now uses a shared route planner. It prefers an already open switcher, then a visible profile or side-menu dropdown, then visible Menu/Profile navigation, then revealing hidden navigation. Feed remains the initial fallback when no supported local entry is available. Visible Menu controls take precedence over a detour through Go to profile.

If the requested actor's own profile is already visible with matching name and own-profile controls, switching returns successfully without opening the sheet. This is navigation confirmation, not UID authorization: callers retain their UID/session checks.

Selection, scrolling, Instagram exclusions, authentication guards, and post-selection confirmation remain shared. An unacknowledged target selection stops rather than restarting and accepting a later screen as success. Navigation failures retain the existing single restart limit.

Tests cover already-active Pages/profiles, open sheets/dropdowns without detours, visible navigation priority and exhausted controls, and saved Feed/switcher layouts. No live switching or posting was performed. The subsequent items 5–8 follow-up also moved name-only callers to the native planner; see pflow-items-5-to-8-20261015.md.

Validation: all 1,579 LDPlayer tests passed. The desktop app built with zero warnings and zero errors.
