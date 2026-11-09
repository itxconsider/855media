# Automatic confirmed-post cleanup scope

The pre-post cleanup sweep previously selected confirmed pending cleanup by LD
index alone. An old index-45 job from another installation or a replaced instance
could therefore block work on the current LD14 index 45.

The sweep now reads the current local instance identity once and defers jobs
whose consistent saved binding identifies a different instance. It records the
deferral, does not contact the device for that job, and does not save or change
the old record. The job remains pending for its original instance or explicit
review. Current-instance jobs retain the same cleanup/binding checks and still
block continuation if their cleanup fails. Unbound or inconsistent legacy records
retain the existing guarded path; missing current identity fails rather than
silently bypassing cleanup. Cancellation remains effective.

Seventeen focused Core tests passed, including six new scope cases. Actual
isolated desktop worker cleanup was invoked twice with an unrelated confirmed
pending record and no device service: both returned successfully, retaining the
history record unchanged. Cancellation also passed. The first diagnostic fixture
lacked confirmed-publication evidence and was corrected; its initial async
harness needed a dispatcher pump, also corrected. Final diagnostic passed.
Main Debug 3.2.4 built with zero warnings/errors after fresh app-absence checks.

No production history edit, device action, post retry or package. The separate
recent-app Clear all warning is unchanged. Evidence: artifacts/cleanup-instance-scope.
