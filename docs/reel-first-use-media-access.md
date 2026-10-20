# First-use Reel media access

The native Reel flow now handles the permission screens shown on a fresh LD before opening the gallery:

1. Facebook Create reel → Allow access to your camera roll → Allow access.
2. Facebook's “Allow Facebook to use your phone’s storage?” explanation → Allow.
3. Android's Facebook photos/media/files permission → Allow.

Recognition requires the expected app package, exact prompt labels and a single enabled visible control. Android Allow is accepted only on the recognized system media permission dialog. Location, microphone, camera and other app prompts are not granted. Foreground Android dialogs prevent tapping Facebook controls in the background. Wrapped prompt text is normalized.

The handler runs only while waiting for the initial native Reel picker, after target verification. It observes a fresh hierarchy after each step, avoids repeatedly tapping a stalled screen, and stops with no media selected if permission setup fails. Existing exact album/file verification and publication checks remain in place.

Validation: focused permission recognition and stalled-dialog tests, plus a complete native Reel dry-run through all three prompts to exact queued media selection without publishing. Desktop build uses artifacts/media-access-build-check to avoid interrupting the running app. This change is pending the next beta package; the running app is not automatically replaced.
