# Workspace Guidelines

## Auto-Accept File Changes
- Apply all file changes and code edits directly without prompting for confirmation.
- Execute builds (`dotnet build`), tests, and verification autonomously.
- Do not stop or pause on routine code modifications.

## Versioning and Packaging
- Versions use `major.minor.beta` (for example, `3.2.1` means major 3, minor 2, beta 1).
- Repackage a beta only after 10 small improvements or bug fixes; do not repackage after each individual fix.
- After 10 beta increments, advance the minor version and reset beta to 0 (`3.2.9` → `3.3.0`).
- After 20 minor versions, advance the major version and reset minor and beta to 0 (`3.19.9` → `4.0.0`).
- Update the release progress below as fixes accumulate and packages are produced.

### Release Progress
- Current packaged version: `3.1.3`.
- Next package: `3.1.4`.
- Completed fixes/improvements toward the next package: **3/10**.
- Latest package: ten accumulated improvements, including 885Helper Android companion, automatic Android helpers and FFprobe setup, Page discovery, account navigation, Share readiness/retries, slower-PC startup allowances, and optional Surfshark checks; see [release notes](docs/release-3.1.3.md).
- Counted changes for the next package:
  1. Replace the header activation key button with a passive subscription remaining-time badge ([180D], [Lifetime], [Expired] or [Inactive]); refresh on license changes and every minute, with the gear retaining Settings access.
  2. Fixed license generation terms: monthly is exactly 30 days, annual exactly 365 days from one UTC issuance timestamp; named/numeric plans select the correct type and invalid terms fail. Existing signed expiries are preserved until reissue.
  3. Remove the external frame-upscaling engine, its Real-World/Animation modes, inference/mux/checkpoint stages and packaging. Preserve native/NVIDIA processing; migrate legacy numeric modes to Fast Native. Fresh publish verified clean. Existing tool-directory deletion was blocked by automatic approval review; inert disk copies remain.

## Subscription Access
- All modules require activated access; do not restore the free trial or always-free downloading.
- App module actions must check subscription access at execution, including dropped files and queued jobs. A disabled UI alone is insufficient.
- Keep Settings → Subscription and Stop actions accessible when access expires. Never automatically retry a submitted post because of subscription expiry.
- Accept RSA-signed licenses bound to this computer, with valid expiry dates for monthly/annual access. Signed Lifetime licenses display Lifetime.
- Online activation must use HTTPS and persist the verified signed token returned by the service. Legacy client-generated serial keys must not unlock modules locally.

## Subscription Duration
- One month is exactly **30 days**; one year is exactly **365 days**, counted from issuance in UTC. Do not use calendar AddMonths/AddYears when generating these plans. Lifetime has no expiry.
- Preserve already signed license payloads and expiry dates. Changing an existing term requires a newly signed license; never rewrite or extend a token locally.

## Upscaling Engines
- Use supported native/NVIDIA processing only. Do not restore the removed Real-ESRGAN engine, bundled model assets, or extracted-frame inference pipeline. Old numeric model values 1/2 migrate to Fast Native (3); NVIDIA retains value 0.
