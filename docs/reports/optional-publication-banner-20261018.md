# Optional publication success banner

An observed publication work cycle can now confirm completion without a success
banner or a profile-caption/time match. Copyright scanning is optional, and no
fixed sequence of states is imposed. Uploading, processing, copyright scanning or
publishing must actually have been observed after this job's Share boundary.
An upload-complete notification by itself and a blank/unknown screen are not
treated as an observed work cycle.

The cycle confirms after 30 continuous seconds with no pending indicators or
progress bars on a recognized Facebook feed, target profile or own Story viewer.
Unknown intermediate screens, refreshed observations and ongoing upload
notifications reset the clear interval. Available Android notifications are read
again at the completion boundary. Existing active uploads from before Share
prevent this completion route from attributing their result to the new job.

Success banners/toasts remain an optional faster route. Profile/Story inspection
remains a fallback when cycle completion is unavailable. No thumbnail matching
has been reintroduced. A confirmed cycle records its observed stages in evidence
and uses the normal configured backup/cleanup behavior for Confirmed jobs.

Facebook error text/description still has priority and records Failed with the
actual message. Errors are checked after refresh/inspection and at timeout as
well as in the polling loop. The pre-Share error baseline stays separate from
the success-message baseline, so refreshing cannot hide a newly observed error.

Validation covers optional copyright, no-banner Reel/Story completion, active
upload blocking, no observed work, unknown screens, reappearing states, refresh
resets and error precedence. The publisher integration tests accelerate only
the clear interval; production retains the 30-second interval. No live posting
was performed.

Logs: `artifacts/cycle-completion-tests.log`,
`artifacts/cycle-completion-regression.log`,
`artifacts/cycle-completion-final-focused.log`,
`artifacts/cycle-completion-build.log`.
