# Personal-account Copy profile link (2026-11-02)

Account Fetch now recognizes the exact enabled Copy profile link control in owned Profile Settings, including after bounded settings scrolling. It resets and verifies the clipboard before re-reading and tapping the fresh control once. The copied URL is checked repeatedly and either yields a direct numeric UID or passes through the existing public share/username resolver. Before accepting it, Fetch rechecks process/foreground guards and fresh settings/Copy profile link controls. Clipboard failure reports an explicit status and cannot reuse previous clipboard contents. Once copied, no further settings scrolling occurs.

31 focused native Fetch/resolver checks passed. After the final settings-proof adjustment, all three new personal-copy cases passed again (numeric URL, share URL, unavailable clipboard). Main Debug built with zero warnings/errors after fresh app-absence check. An overlapping test build briefly encountered a testhost output lock, recovered and passed; no app was terminated.

No live device action, publication, production persistence or package. This change adds the missing personal copy action; it does not install or repair the absent UIAutomator instrumentation observed on LD11. Live clipboard success remains unverified.
