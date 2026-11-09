# Fetch actor snapshot and selection settling

Validated 2026-10-29 after LD-45 failed while reclassifying actors after selecting Alucard.

The native adapter retains the complete native actor name/type list for one invocation, reset by Configure. Verification rebinds those types to a freshly scanned complete switcher: it never reuses selected flags. Changed membership, duplicate/missing names, multiple selected rows and a contradictory Page badge stop the run. Unknown siblings remain Unknown and are not repeatedly probed merely to verify an already established owner. Numeric identity/link checks and process/foreground/input guards remain required; cached roles do not establish UID ownership.

Discovery-only selection taps the intended row once, waits for readable transition frames and opens its verification switcher once. It requires two fresh matching selected observations, with a sixty-second response window, cancellation and fresh process/foreground checks. Blank transition hierarchy reads may wait while process/foreground checks remain enforced; input/authentication/foreign frames still stop. Row/list/tap/scroll/timeout failures now record specific reasons, expected and observed selections and a privacy-aware capture. Settings type discovery can reach the existing own-link evidence with up to six settings-only scrolls instead of only two. Independent actor restoration has a bounded 120-second budget to accommodate the larger selection window and UI reads.

Tests: eleven new cache/membership/selection/slow-frame/partial-unknown/settings-scroll cases; all 122 focused native Fetch, badge-free switching and Android format checks passed in artifacts/fetch-actor-settle/final-validated.trx. Main Debug 3.2.4 built with zero warnings/errors after fresh app-absence verification.

Live LDPlayer 14 index 45 evidence:

- Initial diagnostic optional launch command reported failure although readiness and Facebook foreground were valid; retained separately in the platform probe evidence.
- A separate cold current-screen full Fetch identified both actors and read personal UID, birthday and join date, but the first thirty-second selection implementation rejected Alucard after seeing one correct selected frame; its eight-minute diagnostic/restoration budget then expired. That failed attempt remains in ld45-current and is not reported as a complete Fetch.
- The captured rejection explicitly logged `Expected=Alucard; reason=selection-settle-timeout; observed=Alucard:Unknown`. Slow-observation regression reproduces the insufficient window and passes with the sixty-second budget.
- Final focused live switching proof passed: scan Alucar Trax/Profile and Alucard/Page, select personal actor, select Page, restore personal actor. Every selection returned true; no type-settings discovery repeated. Personal restoration and keyboard restoration passed, managed Graph count zero. Evidence: artifacts/fetch-actor-settle/ld45-switch-proof/native-fetch.log and captured step XML/PNG files. This is switching/verification validation, not a claim that full Page-detail Fetch completed.

No Share/Post, private/root read, LD-56 action, production account/Page/history/queue persistence or package. Saved stores were read-only for the live diagnostics. Optional launch acknowledgment and cold full-Fetch timing are separate remaining limitations. Credit this cohesive fix once: 3/10 toward 3.2.5; existing release ZIPs unchanged.
