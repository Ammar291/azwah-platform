# Referrals module

**Owns:** `SupportReferral`, `SupportRecommendation`, `ISupportReferralRepository`, `ISupportRecommendationRepository`;
`referral.v1` definitions (`ReferralDefinitionParser`, `ReferralDefinitionValidator`) and `ReferralRouter`;
`PartnerOrganisation` (`IPartnerOrganisationRepository`) and the referral queue (`IReferralCoordinationReadModel`)

**Status:** `ReferralRouter` runs after a session is scored, at submission and in staff scoring (D-52, D-53): the single
approved referral rule set for the scope routes the result's tier to support categories; each routed category becomes a
recommendation, and a referral only when the member's referral consent is active. Participants list both
(`GET /api/v1/me/recommendations`, `GET /api/v1/me/referrals`). System administrators keep the partner list
(`/api/v1/admin/partners`); support coordinators see the queue and record manual hand-offs
(`/api/v1/admin/referrals`, D-61). No referral rule set is approved anywhere, and the platform sends partners nothing
(open questions 13 and 33)

**Other modules may use:** `ReferralRouter` inside the scoring transaction; `ListLiveForConsentAsync` for consent
withdrawal

**Must not:** Share anything with a partner without referral consent; claim a hand-off that did not happen; leave a
referral open after the member withdraws referral consent (Consent's `RecordConsentDecision` cancels it)

**Rules that hold** (D-45):

- **Creating referrals:** score and commit, then read the referral consent with
  `IConsentRecordRepository.GetActiveWithShareLockAsync`, add the referral and commit, all inside one
  `IUnitOfWork.ExecuteInTransactionAsync`. The database refuses a referral whose session is not yet Scored.
- **Handing off** (`HandOffReferral`): inside one `ExecuteInTransactionAsync`, read the referral consent with
  `GetActiveWithShareLockAsync` and refuse unless it is the referral's own and active, read the partner with
  `IPartnerOrganisationRepository.GetWithShareLockAsync`, then call `HandOff` and commit.
  The trigger refuses an inactive partner, a partner outside the category or a withdrawn consent anyway, but as a 500.
  `AssignService` has no endpoint.
- **Withdrawing referral consent:** withdraw and commit first, then list the member's open and in-progress referrals for
  that consent and cancel them, then commit, inside one `ExecuteInTransactionAsync`.
