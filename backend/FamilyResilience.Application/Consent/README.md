# Consent module

**Owns:** `ConsentPolicyVersion`, `ConsentRecord`; `IConsentPolicyVersionRepository`, `IConsentRecordRepository`; the
consent use cases

**Status:** `GET /api/v1/consent/policies/current`, `POST /api/v1/consent` (grant or withdraw) and
`GET /api/v1/me/consents` (D-51). A grant needs the current approved policy, an active household and a member of 18 or
over; withdrawing referral consent cancels the referrals that relied on it. No reviewed consent text exists (open
question 9), so no policy is approved outside test databases

**Other modules may use:** `IConsentRecordRepository.GetActiveAsync` and, inside an explicit transaction,
`GetActiveWithShareLockAsync`

**Must not:** Infer consent; let another module read consent tables directly; grant against an unapproved or superseded
policy; grant consent to members of a withdrawn household (open question 25) or to members under 18 (adults only, D-58)
