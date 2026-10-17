# Dispatch queue survives restart

The dispatch queue previously existed only in the AutoPost view model's ObservableCollection. Its constructor called InitializeSampleTasks, which cleared that collection. Page settings and started P-Flow jobs were persisted separately, but manually queued rows were not saved. A restart therefore discarded added queue entries.

Queue rows now persist immediately to `%AppData%/855Media/facebook_dispatch_queue.db` when added, removed, cleared or when their notifying fields change. SQLite uses FULL synchronization. Each row has a stable queue ID, preserves caption/path/target/owner/immutable LD binding, and is linked to its reserved P-Flow job before execution. Updating a row does not move its queue position. Explicit Clear queue persists the empty queue.

Startup restores saved rows rather than clearing them. Linked jobs are initially held for outcome reconciliation; history recovery then restores their actual outcome. Unknown and weakly confirmed publications remain review-only. Only an explicitly queued job or an interrupted job recovered before submission becomes Queued again. No run starts automatically. Missing history and unreadable storage are reported; the saved queue is not silently replaced on a read failure.

The previous version never saved unstarted rows, so entries already lost before this change cannot be reconstructed reliably from the queue file. Existing publication history remains separate and is not used to invent an old queue.

Verification: 35 queue-storage, routing and reserved-media tests passed, including new-store restoration, bindings/captions, updates/order, removal/clear, interrupted statuses, weak publication evidence and corrupt-row preservation. Desktop build succeeded with zero warnings and errors. Tests used temporary databases.
