# LD-55 share chooser verification

Tested on LD-55 using the failed Bushcraft improvisation job media. No Share/Post control was pressed and no publication was submitted.

## Observed routes

- Preselected Reels: Android displays Share with Reels, JUST ONCE, ALWAYS. JUST ONCE opens the Reel editor.
- Alternative Reels: Android displays Share with Feed with Reels under Use a different app. Tapping Reels immediately opens the Reel editor, without a second JUST ONCE step.
- The reverse alternative Feed route also launches directly.

The failed run logged Selecting share destination: Reels followed by Share chooser and Just once were not verified. Its saved diagnostic was already on Reel Settings. The live alternative route reproduced the transition that caused this rejection.

## Change

Keep per-job evidence of an acknowledged tap on the requested chooser destination. A matching composer can follow either this tap or the preselected destination plus JUST ONCE route. Opening a composer without this job's chooser evidence remains rejected. If the chooser remains open, JUST ONCE and destination-change checks still apply. Existing identity and final submission checks remain in place.

## Validation

19 focused share chooser tests pass, including alternative direct launch, rejected destination tap, preselected destination changes, and direct composer without chooser rejection. Desktop build passes with zero warnings/errors.

Live XML captures are in artifacts/ld55-share-chooser. The live test stopped at the Reel editor; it did not execute a complete public posting run with the patched app.
