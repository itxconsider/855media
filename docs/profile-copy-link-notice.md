# Personal profile link-sharing notice

The user confirmed manually tapping Continue on LD71's “Link sharing has been updated” prompt. The earlier observed continuation therefore was not automatic recovery. Copy was requested at02:42:38 and link verification began at02:43:37.

Native link lookup now handles the exact owned sharing notice after either a personal Copy profile link action or a Page-link action, before clipboard polling. It requires the exact notice title/body, safe Facebook hierarchy and one enabled visible clickable Continue control. It revalidates the Fetch context and binds to a fresh prompt/control before tapping once. A subsequent frame must leave the prompt; persistent/incomplete/ambiguous notices fail without repeated taps or clipboard polling. The same invocation-owned notice is dismissed on cleanup for both actor types. Existing process/foreground, copy-marker and repeated clipboard/link verification remain.

All92 focused checks passed, including8 new cases: personal-profile notice success before clipboard reads, persistent notice, incomplete notice and disabled/duplicate/foreign/input/different-prompt rejection. Existing Page notice, clipboard helper, selection reuse and Settings restoration checks passed. An initial synthetic positive case had no copied-link value configured; its fixture was corrected before the successful final run.

Isolated desktop Debug3.2.4 built zero warnings/errors. Desktop/test Core SHA256 match `E0D96ECCFFD6E4B995382360073CDFA9559BDD616C9C8A247273621965977FA8`. Main app was running, so active outputs were retained. The already-acknowledged live notice was not recreated; no live automatic-Continue claim, production data/settings/history changes, publication, Graph, keyboard/Recents changes or package.

Evidence: `artifacts/link-notice/tests/final.trx`; updated build `artifacts/link-notice/desktop`.
