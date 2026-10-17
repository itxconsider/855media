# ScreenTime interaction fixes, items 1–5

Empty media types are skipped before upload/history idle checks. The queued-job filter includes the assigned binding, target, profile/Page flag, type, and Queued state.

Read-only captures from LD-21 are saved under docs/fixtures/pflow-screentime/ld21-*.xml. The standard hierarchy capture initially returned empty; the forwarded ATX hierarchy endpoint succeeded. The user manually opened the reaction banner, applied Love, typed a comment, and published it. The agent did not tap Like, choose a reaction, type, or send. A daily-limit interruption is saved as a negative fixture.

The captures identified these concrete compatibility issues:

- Feed metadata uses `Shared with: Public` rather than the older label.
- Post Like icons expose a long `Like button.` accessibility description.
- Applied Love exposes `Love button, pressed. Double tap and hold to change reaction.` The reaction-summary control is not sufficient confirmation.
- The RopeStyleUFIDockView banner has overlapping virtual-node bounds. A layout-specific adapter partitions the complete seven-choice dock span into ordered icon slots. Unknown overlapping layouts remain rejected. Automated live tapping of this adapter was not tested.
- The comments sheet exposes an owner-specific EditText with duplicated text/accessibility labels. After typing, the owner remains in content-desc, while the text becomes the draft.
- The Send arrow exposes `Send`. That alias is accepted only with an owner-specific Facebook comment input and recognized comments-sheet evidence.
- Published comments expose separate author, timestamp, body, and Reply controls. Confirmation now checks author and exact body within one comment row; input text and neighbouring comments cannot substitute. The Send arrow need not remain visible after publication.

Existing post-identity guards remain. These live comment-sheet captures expose no numeric post ID; recognizing the sheet or seeing a matching author/text alone does not authorize a send or confirm the intended post. Random comments without a numeric target ID remain skipped. An adapter providing trustworthy post association is separate work.

Tests cover live XML recognition, supported aliases, unknown/partial surfaces, hidden controls, selected-state confirmation without toggling, and one-send comment confirmation after Send disappears. Captured and synthetic fixtures are distinguished by their filenames and test setup.

Validation: all 1,595 LDPlayer tests passed. The desktop application built with zero warnings and zero errors using an isolated output directory.
