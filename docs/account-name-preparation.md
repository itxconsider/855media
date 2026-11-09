# Account name preparation and diagnostics

Implemented 2026-10-29 after LD-56 account preparation repeatedly opened the switcher and used legacy UID-recovery restart despite name-only mode.

The posting fallback navigator now receives the same scoped saved target mappings as its initial shared account observation. PFlowNativeAccountNavigator uses the shared mapping-aware observer/selector for personal recovery. The existing Fetch constructor path also benefits; callers without mapping data retain their existing observer behavior.

Before navigation, name-mode account preparation waits up to sixty one-second observations for a safe loading-only response, rechecking worker/foreground and cancellation. A persistently loading frame fails without navigation. Late headers proceed normally. Name-only recovery disables nested selector restart; unresolved name/type evidence does not enter legacy UID-recovery restart. The legacy UID mode is unchanged.

Each discovery-open invocation taps its dropdown once and waits up to twenty seconds for the switcher response. It cannot repeatedly tap the same dropdown within that invocation. Separate explicit recovery calls remain bounded and may open the switcher again.

Unresolved shared account observations record expected owner and parsed row names/types/selection and capture the switcher before dismissal. The native navigator records expected and observed selected name/type or explicit unavailable/ambiguous status and captures its failure surface. The automatic session exception carries that failure detail. Existing diagnostic redaction and editable-input screenshot suppression apply; failure to capture never substitutes for the verification result.

Posting still requires the unique saved account binding/name. The new first-time native settings type discovery is in Fetch, not an automatic authorization fallback for unmapped posting accounts. Unknown layouts remain unresolved; no Graph, live UID or private-storage fallback was added.

Validation: four new native preparation cases cover mapped badge-free personal selection, loading timeout with no taps/restarts, a late header and cancellation. All 300 focused native-account, badge-free, posting-name, ScreenTime-name and Fetch checks passed: artifacts/account-name-preparation/validated.trx. Main Debug 3.2.3 built with zero warnings/errors after the user initially requested keeping the app running, then explicitly closed it and absence was verified. No alternate desktop output or process termination.

No live LD-56 action, device test, job retry, publication, production data edit or package was performed. No saved capture of the latest 01:51 account-preparation failure existed; that exact live failure has not been replayed or claimed fixed end to end. New captures will identify the next unrecognized surface. The older 01:17 previous-upload hold is a separate issue and is unchanged.

One completed improvement credited: 8/10 toward 3.2.4. Release archives unchanged.
