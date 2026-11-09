# Android 14 compatibility validation

Work in progress, 2026-10-30. Packaged version remains 3.2.4. Live tests use
LDPlayer 14.0.32 at `C:/LDPlayer/LDPlayer14`, index 45 only. LD-56 is excluded.
Diagnostic posting is intercepted before publication and uses synthetic media,
isolated history and read-only saved account mappings.

## Implemented

- Launch Facebook through its uniquely resolved package launcher activity and
  verify Android's launch acknowledgment. Use the legacy monkey command only
  when activity resolution is unavailable; Android 14 lacks that binary.
- Recognize LD14 Quickstep's exact overview container, horizontally reveal its
  Clear all control and verify empty recent tasks with Android 14 task output.
  Foreign/ambiguous/disabled controls remain rejected.
- Before direct ADB routing, verify that the host listener belongs to the VM
  listed by the selected installation and that its player executable belongs
  to that installation. Missing explicit installation paths never fall back
  to another installation. Failed forwarding cannot authorize an existing
  HTTP daemon. Host port collisions fail closed.
- Recognize the captured compact photo Gallery toolbar, AutoCompleteTextView
  caption field and bottom Next button. Editable content is redacted and cannot
  serve as publication evidence.
- Share verified keyboard activation across HTTP/direct callers. A listed exact
  helper rejected as an unknown input method on SDK 34+ gets a bounded settings
  fallback with exact readback. Other errors/old Android/missing helpers do not.
- Recognize the owned Add text editor's unique enabled Done button after exact
  caption readback; require the caption to survive return to the composer.

## Evidence and limits

Artifacts are under `artifacts/ld14-compatibility`.

- `validated-current.trx`: 106 compatibility checks passed.
- `keyboard.trx`: 7 activation/fallback/negative/cancellation checks passed.
- `caption-editor.trx`: 55 photo-entry and keyboard checks passed.
- `validated-final.trx`: 176 focused photo, keyboard, launcher, recents,
  transport, routing, permission and Android format checks passed.
- `cleanup/platform-suite.json`: live instance identity, internet, hierarchy,
  screenshot, Home, Facebook launch/focus, recent-app cleanup, Back, Facebook
  return and normal-keyboard restoration passed. Clear all is initially hidden;
  the production reveal/cleanup succeeded.
- `photo-keyboard-retry`: transferred/selected one photo and read back the exact
  caption after keyboard delivery, then stopped at the separate Add text editor.
  Generated media cleanup verified; Graph requests 0, publication attempts 0.
  The subsequent Done handling was added in response; this earlier failed run
  is retained rather than represented as a complete photo pass.

- `photo-caption-done`: live single-photo dry run reached ComposerReady after
  exact caption verification, Done and final photo preview; Share taps 0. Generated
  photo cleanup verified. This validates the keyboard fallback's actual text
  delivery, not merely a settings readback.

- `photo-multi`: selected all three synthetic images once, verified their
  fingerprints and caption, and reached ComposerReady; Share taps 0.

- `story`: live exact single-file Story album selection reached the Story editor
  with its Share control available. File size/hash/media ID were verified;
  no Share tap occurred. Screenshot visually checked.

Full Reel and ScreenTime results are pending. Earlier
Reel testing stopped on a foreign foreground during processing; a new capture
point preserves that screen before recovery. Simultaneous LD9/LD14 operation at
the same index is not certified by these sequential single-device probes.
Previously documented full-suite failures are not declared fixed by focused
tests. No package or production account/history edits are included.

Continuation check: the Reel process no longer exists and produced no final summary. LD14 index45 is stopped. The ScreenTime invocation found no running target and therefore does not count as validation. Release counter recorded once at 9/10; remaining live tests are still pending.
