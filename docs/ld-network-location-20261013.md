# Running LD public IP and location

The Devices · Live step status table now includes an IP location column with
the city on the first line and the public IP below it. Refresh IPs retries all
running rows. The separate ScreenTime personal-profile session card was removed;
ScreenTime configuration remains in Run settings for PFlow.

## Lookup and lifecycle

- Read `https://api64.ipify.org?format=json` using curl inside the assigned LD.
  Never substitute the Windows host's public IP or the emulator's private address.
- Resolve the observed IP through `https://ipwho.is/{ip}` inside the LD, restricted
  to success, IP, city, region and country fields. Validate that the returned IP
  matches before using the city. Location is approximate IP geolocation, not GPS.
  A per-app VPN may route Facebook differently from the guest shell.
- Keep network requests separate from UI input, Facebook identity and worker
  status. Probe at most three LDs concurrently, with bounded curl requests.
- Scan existing LD runtime state every 30 seconds. Probe running rows at startup,
  when they start and every five minutes; failures retry after one minute.
  Failed runtime scans do not mark all instances stopped.
- Cache location by public IP for six hours (failed location lookups five minutes)
  and share the cache between rows. An IP change triggers a new location lookup.
- Cancel and clear on stop, device-list replacement or view-model disposal.
  Ignore late results from canceled probes and keep stopped rows blank.
- Preserve an observed public IP when only the location request fails; show
  Unavailable when the guest cannot report an IP. HTTPS certificate checks remain
  enabled. No credentials, account IDs or Facebook data are sent to either API.

Provider references: [ipify API](https://www.ipify.org/) and
[IPWhois documentation](https://ipwhois.io/documentation).

## Validation

- 22 focused core tests passed: address validation, malformed output, location
  mismatch, shared-IP caching, changed IPs, outages and cancellation.
- `tools/LDNetworkCheck` synthetic view-model check passed: running-only probes,
  late response after stop, fresh IP on restart and disposal.
- Existing `tools/PFlowSelectionCheck` passed: selection still performs no LD
  commands; 78-row select-all coalesces to one refresh.
- Read-only live check passed on five already running LDs:
  LD-3 Los Angeles, LD-4 Miami, LD-5 Detroit, LD-6 New York City, LD-7 Denver.
  Each returned a different public IP. No Facebook interaction or publication.
- Solution build passed with zero warnings and errors.
