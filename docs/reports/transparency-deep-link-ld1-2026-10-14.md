# LD-1 Transparency direct-link test — 14 October 2026

LD-1 was initially shut down and was started for this navigation-only test. Its current Graph token identified **NYC Fixes**, Page ID **61594783866492**. No account switching, login, publication or settings changes were performed.

| Candidate | Link accepted | Transparency confirmed |
|---|---|---|
| `https://www.facebook.com/profile.php?id=61594783866492&sk=about_profile_transparency` | Yes | No — Page profile opened |
| `https://www.facebook.com/61594783866492/about_profile_transparency/` | Yes | No — profile or Feed surfaced across repeated checks |
| `fb://facewebmodal/f?href=` plus the encoded first URL | Yes | No — Page profile opened |

The first attempt lost its ADB forward and produced empty screen-reader results; those results were not used to decide link support. The forward was repaired and the candidates were tested again with actual hierarchy and screenshot captures. Neither repeat confirmed a Transparency screen.

As a control, the production `PFlowProfileDetailsReader.ReadOwnProfileAsync` header/Transparency route succeeded and returned the Page creation date **September 29, 2026**.

Conclusion: these candidate links are not a reliable direct Transparency route on the tested LD-1/Facebook build. Keep the verified profile header → Transparency lookup for this layout. The result does not establish that every Facebook build or web browser handles the URLs identically.

- [Final test results](/D:/repos/855Media/artifacts/transparency-ld1-20261014-final/transparency.json)
- [First link screenshot](/D:/repos/855Media/artifacts/transparency-ld1-20261014-final/link-1.png)
- [Second link screenshot](/D:/repos/855Media/artifacts/transparency-ld1-20261014-final/link-2.png)
- [Third link screenshot](/D:/repos/855Media/artifacts/transparency-ld1-20261014-final/link-3.png)

LD-1 remains running after the test. The production app's navigation code was not changed.
