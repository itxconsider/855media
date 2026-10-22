# Grading workspace usability

Updated 2026-10-23 following the cramped/cropped grading preview report.

The studio now uses a height constrained by the available window instead of a 360px capped StackPanel. Its header stays separate from bounded content; Larger preview / Show more queue adjusts the workspace height while retaining the queue toolbar. The preview receives more horizontal space, fits the complete image at 1x and retains existing zoom controls. Scrolling the controls no longer changes preview framing. The histogram lives in the inspector rather than consuming preview height.

Segoe UI, larger labels, brighter text and taller slider targets improve readability. Temperature, contrast, saturation and brightness have numeric inputs alongside their existing sliders. The preset selector has its own row and actions below it, preserving readable names at laptop widths. Queue list content is clipped to its remaining area so the empty state cannot cover queue actions when the studio is expanded.

Validation: actual VideoEditorView and VM rendered at 960x560, 1180x760, 1920x1080 and back to 1180x760, using a synthetic landscape bitmap. Frame heights were 191/328/431px. Inspector scrolling, stable return sizing, expansion/restoration, numeric two-way brightness binding, zoom/reset and grading reset passed along with existing responsive/browser/modal checks. Settings and the final diagnostic queue use isolated paths; no jobs were executed. The initial diagnostic inadvertently loaded the saved queue for display before queue-path isolation was added. Desktop build passed with zero warnings/errors. Native signed-in workflow and physical display/DPI validation remain pending.

Rendered previews: `artifacts/responsive-ui/grading-960x560.png`, `grading-1180x760.png`, `grading-1920x1080.png`. Updated isolated app: `artifacts/instagram-browser-build/855Media.exe`. No running process was restarted and no release archive was produced. This is one improvement credit, bringing accumulation to 20/10; credits are consumed only after a verified package.
