# Professional profile verification — October 13, 2026

The installed Facebook layout on LD index 35 uses Profile Settings → Copy profile
link instead of an exposed Your profile link URL. The copied value is a Facebook
share URL without a numeric UID. The old verifier could reach the personal
profile but could not extract live identity evidence from those settings.

The native navigator now supports the Copy profile link control after opening
settings from an observed own-profile surface. It replaces the Android clipboard
with a unique non-secret marker and confirms that marker before tapping a fresh,
scoped Copy control. It only accepts supported public Facebook links from the
result. An unchanged clipboard, unrelated contents, Page settings, editable
forms, ambiguous controls or authentication challenges cannot establish proof.

Numeric links are read directly. Share links are resolved through public Facebook
redirects or canonical metadata to a numeric profile URL. The default HTTP client
uses no cookies or authentication, checks every redirect's HTTPS Facebook host,
limits redirect hops and response size, and uses a bounded timeout. Unrelated
links in the response, conflicting canonical UIDs, external redirects, login
pages without canonical identity, vanity-only links and cancellation fail closed.
After obtaining a UID the navigator returns to the own-profile surface; the
automatic session service still compares it with the mapped personal UID.

The clipboard RPC follows the [OpenATX clipboard API](https://github.com/openatx/uiautomator2/blob/master/uiautomator2/__init__.py).
Professional mode stays enabled. No login, credential submission or publication
is required for this verification route.

## Validation

- Solution build: zero warnings/errors.
- Focused PFlow/ScreenTime/AtxAgentClient/target-refresh suite: **831 passed,
  zero failed/skipped**.
- Live LD index 35: Roxy Pritchard authenticated, exact UID `61593807750525`.
- Live LD index 33: Rudolph White authenticated, exact UID `61586306443979`.
- LD index 31 inspection still returned Unavailable and its observed final screen
  was the Feed. Its separate navigation problem is not certified as fixed by this
  change.

Live evidence is in the local temporary `855Media-login-inspect35-20261013` and
`855Media-login-inspect33-20261013` report directories. Input values are redacted;
clipboard contents, raw public response pages and credential values are not
written into diagnostic reports.
