# LD-56 unreadable switcher recovery

2026-10-30: The 18:21 capture shows Bros Cooking active and Marc Aurelius in a visible switcher. Accessibility contains only the disabled background Menu. Existing account header reading already recognizes the Page/owner pair; personal recovery was attempting switcher selection again.

Name-only account recovery now recognizes the exact owner shortcut as recovery evidence, backs out at most three times to a recognized Feed, reopens Menu and requires a fresh unique enabled visible in-viewport shortcut in the active header row before one tap. The disabled background cannot authorize a tap. The existing personal name/type observer must then confirm the result. Wrong names, foreign packages, disabled controls and duplicate shortcuts remain rejected; unresolved navigation stops without restarting Facebook or calling Graph.

Validation: 193 focused header/posting/native-account/response tests passed, including the saved capture and shortcut negative cases. Main Debug 3.2.4 built with zero warnings/errors after app-absence verification. No live LD-56 action or publication. The complete live recovery sequence remains unvalidated; these tests cover capture interpretation and existing recovery regressions, not a successful live retry.

Artifacts: artifacts/ld56-menu-shortcut/validated-final.trx. Fixture: docs/fixtures/pflow-layouts/ld56-menu-overlay-missing.xml.
