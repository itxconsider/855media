# Queue preparation loading and speed

Preparing the post queue now shows a blocking animated progress indicator with stages for checking media, selecting a target's files, adding selected files and saving. The preparation command rejects concurrent selection and running/busy operations. The busy flag and overlay clear through `finally`, including empty selections and failures. Existing subscription checks remain at execution.

Previously, each added row opened SQLite and committed its own durable write, and each collection change rebuilt dashboard subscriptions across the entire queue. Selection now buffers row writes and commits one ordered transaction on a worker thread. Source grouping and caption reads also run off the UI thread. Random file selection uses a linear-time shuffle instead of sorting random keys. Row additions yield every 25 files so rendering and the progress animation can continue. The dashboard refreshes its row subscriptions once at completion.

Queue order is taken from the task collection rather than the write-buffer set. Existing saved rows remain intact. Invalid batch input writes nothing; an insertion error rolls back the batch. Save errors are reported, the visible queue remains in memory and pending writes are retained for a later save attempt. No post is submitted during preparation.

Validation: 64 focused queue/group/count tests passed. New batch tests cover 250 ordered rows with metadata, existing-row preservation/upsert, invalid input and SQLite trigger failure rollback. Desktop build passed. A local 500-row benchmark with schemas/serialization warmed measured 1000.5 ms for individual saves and 4.9 ms for one batch. This comparison measures queue database saving only; total selection time also includes source scanning, history/content checks and captions. It is not a claim of equivalent speedup for the whole workflow.

Test build: `artifacts/queue-selection-build/855Media.exe`. Existing live queues and media were not changed by verification. No distribution package was produced.
