# Append text to original captions

The Page/Profile configuration editor has separate optional **Append to caption** fields for Reels and photos. Text can contain multiple lines, hashtags and emoji. The Reel setting also applies to the additional saved Reel source.

Preparation first resolves the original caption from the filename or the matching `.txt` sidecar according to the existing per-type option, including the existing filename fallback for missing/empty/unreadable text. Nonblank append text is trimmed at its outer edges and joined with one newline; internal formatting and the original caption are preserved. Blank append text leaves existing behavior unchanged. Stories retain their existing no-caption behavior.

Save the target configuration, then prepare new queue rows to apply the text. Prepared row captions are snapshots: editing this setting does not modify existing rows. The job creation fallback uses the same resolver, while rows with a caption retain that caption without a second append.

The new settings default to empty for older JSON, survive model/view-model sync and JSON round-trip, and participate in the existing editor draft, Save and rollback property list. Cancel does not modify the active settings. No source media or `.txt` files are rewritten.

Validation on 2026-10-26:

- All 13 focused caption/media-source/Part tests passed: `artifacts/caption-suffix/focused.trx`.
- Actual headless desktop VM/queue diagnostic passed independent Reel/photo suffixes, Story isolation, already-prepared row preservation, save/reload, editor property coverage, actual two-way field binding and Cancel isolation. Existing multi-type/daily-limit/duplicate/path-reset/account-table checks also passed.
- Actual editor rendered at 1280×820 and inspected: `artifacts/caption-suffix/editor.png`.
- Main Debug version 3.2.0 built with zero warnings/errors after confirming 855Media was closed. No live Facebook/device action or package.

Run the headless checks with `dotnet run --project tools/diagnostics/AccountTableCheck/AccountTableCheck.csproj -p:Version=3.2.0 --no-restore`.

Follow-up layout improvement (2026-10-26): Move **Keep video parts together** beside **Use title from text file** in a horizontal WrapPanel. Both existing bindings remain unchanged; the row can wrap when narrow. XAML structure/binding checks, the existing desktop queue/editor diagnostic and a fresh 1280x820 render passed. Main Debug 3.2.0 built with zero warnings/errors after the user closed the app. This separate validated layout improvement adds one credit (15/10); no package.
