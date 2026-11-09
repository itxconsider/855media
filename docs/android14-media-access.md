# Android 14 camera-roll permission sequence

Supported sequence: Facebook Allow access screen, Facebook camera-roll confirmation
dialog, then Android media permission (Allow all), followed by the gallery.
The missing case was the exact Facebook camera-roll dialog captured in the LD14
dry run. Match its exact title/body, Facebook ownership, Deny label and one usable
Allow control. Preserve foreground, visibility, ambiguity and no-repeat guards.
The Android Allow all action was already supported via the owned permission
controller's exact media question/resource ID; tests now cover its three-option
Android14 layout and complete three-prompt sequence. No permissions unrelated to
media are granted.

Nine focused checks passed (four new captured/sequence/negative cases), including
the exact saved LD14 dialog and confirmation-button bounds. Main Debug 3.2.4 built
with zero warnings/errors after fresh app-absence checks. No new live permission
tap, device test, publication or package. Live end-to-end retry remains pending.
The empty gallery in the user's last screenshot does not prove a transfer failure:
the earlier dry run had already removed its generated video during verified cleanup.
