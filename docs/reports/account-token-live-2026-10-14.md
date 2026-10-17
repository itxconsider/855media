# Live `/me?fields=id,name` comparison — 14 October 2026

Tested the ten currently running instances, LD-11 through LD-20. Tokens were read from each assigned instance's Facebook authentication record, held in memory, and sent in an Authorization header to `https://graph.facebook.com/me?fields=id,name`. Tokens and raw authentication records were not saved or printed. No UI navigation, account switching, credential entry, or profile-settings lookup was performed.

Eight requests returned HTTP 200. Five matched the underlying personal-account UID and root account name. Three returned Page identities instead. Two instances had no readable current token and were not queried.

| LD | Root personal name | Graph name | UID/name comparison |
|---|---|---|---|
| 11 | Try Dana | mattea.ingemi | Different: Graph returned a Page |
| 12 | Kolarah Smith | Kolarah Smith | Both match |
| 13 | Aldous Ran | Aldous Ran | Both match |
| 14 | Omega Anderson | Omega Anderson | Both match |
| 15 | Irene White | Irene White | Both match |
| 16 | Not available | Not queried | No readable current token |
| 17 | Julius Eusebio | Julius Eusebio | Both match |
| 18 | Not available | Not queried | No readable current token |
| 19 | Randall Connolly | Caprani Plumbing & Heating | Different: Graph returned a Page |
| 20 | William Rhodes | Caprani Plumbing+ | Different: Graph returned a Page |

The differing IDs on LD-11, LD-19 and LD-20 each match a Page record discovered on that same instance. The record's owner UID matches the root underlying personal UID. A second read-only request on those three devices reproduced the same result. This establishes the token's Page identity and its association in root records; it does not independently verify live Page-management permissions.

All eight queried instances retained the same root UID and token before and after the comparison. No own-profile screen was already visible, so an on-screen own-profile name comparison was not performed. The test compared Graph names to root account names and saved mappings.

## Consequence for account verification

`/me` cannot always be equated with the underlying personal UID. It can resolve to the selected Page's token identity. A successful personal-account match is useful evidence for a fast path, but a Page result should be classified separately rather than treated automatically as a wrong login. Current own-profile/personal-mode checks and fallback UID navigation are still needed before claiming that a requested personal actor is active.

Names are a consistency check, not unique identity proof. Missing tokens remain unverified rather than inferred from saved account records.

## Artifacts

- [Full comparison, including UIDs](/D:/repos/855Media/artifacts/account-token-live-20261014/report.json)
- [Follow-up Page identity comparison](/D:/repos/855Media/artifacts/account-token-live-20261014/persona-check/report.json)
- [Reusable read-only test harness](/D:/repos/855Media/tools/AccountTokenLiveCheck/Program.cs)

The first requests were rejected locally before receiving an HTTP response because binary light-preference framing was included in the token field. Extracting the token alphabet without that framing resolved this; the successful results above use the corrected parser. No production verifier was changed by this test.
