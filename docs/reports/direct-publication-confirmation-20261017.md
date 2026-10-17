# Direct Facebook publication confirmation

Publication checks now keep Facebook on its post-submit screen rather than scrolling Feed/Reels while awaiting a result. Fresh success banners and toasts remain the first confirmation route. Toasts are cleared before Share and checked before and after hierarchy reads.

An upload-complete notification starts a brief grace period (up to 8 seconds, bounded to one quarter of the configured confirmation window). An explicit publication success during this period confirms the job directly. Otherwise upload-only evidence falls back to target-profile caption/author/time verification. An ongoing upload clears the pending completion signal. Phone-verification prompts and publication errors take precedence over success.

Success recognition normalizes whitespace and terminal punctuation and accepts explicit shared/published/posted/live wording for the correct post type. Pre-existing messages, hidden nodes, editable fields and other app hierarchies cannot provide direct UI evidence. Generic upload/processing messages remain insufficient.

The trace event `publication.confirmation-route` distinguishes `direct-app-message`, `direct-app-toast` and `profile-fallback`, including the fallback reason.

Validation uses synthetic unit and publisher regression tests, including upload completion followed by a later direct success. No live post was published during this change.
