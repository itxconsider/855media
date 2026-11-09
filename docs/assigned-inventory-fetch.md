# Assigned inventory reads for Fetch

The LD6 report rejected a 36-line console response after ten parsed rows. Historical diagnostics do not identify the exact bad field. Assigned Fetch/binding/ADB checks now validate the uniquely indexed assigned row independently of unrelated malformed rows with known other indices. Unknown indices, duplicates, a missing assigned index and malformed assigned fields still fail. Full inventory used for launch capacity remains strict; an assigned result cannot authorize a full-inventory admission decision. Numeric console fields use invariant culture.

Raw console reads share only an in-progress command, with independent caller cancellation, bounded retries and no completed-result cache. Exact installation, device, binding and transport ownership checks remain in place.

Validation: 99 focused Core checks passed, including assigned/full parsing, culture, raw sharing, launch capacity and native Fetch cases. Read-only live LD9 index6 returned one running assigned instance; strict inventory returned36 rows. This does not reproduce or prove the historical field failure. Main Debug3.2.4 built with zero warnings/errors; Core hash matches the tested artifact. No Facebook navigation, Graph, publication or production data changes.
