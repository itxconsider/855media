# Graph-first identity routing for ScreenTime and PFlow

The shared target verification used to select the personal profile and requested persona before asking Graph for the live UID. Even a matching active account could therefore open the native switcher. Account preparation also used the native account navigator before the operation-level check.

The new route is:

1. Validate the device binding, boot/network readiness, assigned app foreground and authentication state.
2. Read fresh Graph identity during account preparation. A matching underlying owner preserves the current persona; unavailable or different-account sessions retain the existing native account recovery.
3. Read fresh Graph identity for every ScreenTime verification callback and PFlow job.
4. When owner and active target match, verify any distinct Page persona delegation and proceed without persona selection or settings UID lookup.
5. When the underlying owner matches but the actor is different, select the mapped target once, then require another fresh Graph match. An acknowledged switch alone does not authorize work.
6. When Graph is unavailable, require the existing fresh UI UID lookup. Known owner contradictions and failed Page delegation remain failures.

ScreenTime does not need a profile visit for a complete Graph match. PFlow still opens the already selected actor's own profile through the existing profile navigation helper, without selecting a persona, because publishing requires own-profile controls. Authentication and own-profile/composer checks remain before media staging. Queued-job mapping validation and device leases are preserved.

The Graph-first decision logic is in `PFlowLiveUidVerification.ResolveAsync`; `RequireAsync` remains the strict final UID validator. The shared ViewModel verification route no longer performs unconditional root-owner/profile/persona navigation. Account preparation records `account.uid.graph.ready` when its Graph match skips native account navigation; target verification records its existing `identity.live-uid` evidence.

Validation: 101 focused live-UID, Graph-reader, profile-navigation and session-workflow tests passed. New route cases cover personal, canonical Page and delegated Page matches with no UI callbacks, different actors with a second Graph read, unchanged UID after acknowledged switching, Graph-unavailable fallback, wrong owners, failed delegation and cancellation. Release build succeeded. The app output path remains `artifacts/startup-ready-fix`.

No live Facebook switches or publications were performed for this change.
