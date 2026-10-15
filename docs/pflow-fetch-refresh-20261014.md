# Pull-to-refresh recovery during Page fetch

Page fetch can now attempt one downward pull gesture per stalled stage:

- Page home: when the expected Page is confirmed but its dashboard control is absent.
- Professional dashboard: after the existing 30-second Analytics-tab readiness wait fails.
- Analytics: after the existing 30-second content readiness wait fails.

Recovery rescans the current screen before using gesture bounds. It requires the
recognized fetch surface, rejects input/composer/login/switcher contexts, and
starts below upper navigation controls rather than the Android status bar. The
downward drag lasts 800 ms. Its acknowledgement does not establish readiness.

After the gesture, the helper checks fresh content at a 400 ms base interval, with a
10-second elapsed deadline and a 25-check cap. A temporary empty render can settle
within that bound; a different recognized context stops recovery. There is no
second refresh within the helper. Failed stages retain the existing fetch behavior
for unavailable details instead of treating the gesture as a successful lookup.

The subsequent fetch-pacing change adds 500 ms to those intervals (900 ms in
production), while preserving the elapsed deadline and check cap.

Trace events: `page.home.refresh`, `page.dashboard.refresh`, `page.analytics.refresh`.

Validation: 74 focused refresh, Page navigation, hierarchy and profile-date tests
passed. Application build passed with no warnings or errors. No live Facebook
refresh or publication was performed for this change.

Build: `C:\Users\itxco\AppData\Local\Temp\855Media-fetch-refresh-build\855Media.exe`.
