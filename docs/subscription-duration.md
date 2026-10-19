# Fixed subscription durations

Monthly: 30 days. Annual: 365 days. Lifetime: no expiry. Terms start at a single UTC issuance timestamp, not activation. Month length and leap years do not change the term.

The key generator accepts monthly/30, annual/yearly/365, lifetime/0. Unsupported values fail rather than silently generating a lifetime key. It now marks a 30-day key Monthly instead of Annual. Signature verification and existing signed expiry dates remain unchanged. The license issued October 18, 2026 with November 18 expiry remains valid according to that signed date; changing it requires reissue by the signer.

Validation: fixed-duration and LicenseService tests passed, including leap years, calendar boundaries, invalid terms, signed activation, and expiry enforcement. Key generator and desktop Debug builds passed. No production key/token was generated or modified in this change. No beta repackaged; improvement 2/10 toward 3.1.4.
