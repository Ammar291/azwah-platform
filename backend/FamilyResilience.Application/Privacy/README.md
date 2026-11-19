# Privacy module

**Owns:** `PrivacyRequest` (`privacy_requests`): a family's request about its data, for access, correction or erasure;
the data export (`ExportMyData`)

**Status:** Built (D-89). `GET /api/v1/me/data-export`, `GET` and `POST /api/v1/me/privacy-requests` for participants;
`GET /api/v1/admin/privacy-requests` and `POST …/{requestId}/close` for program administrators. Erasure itself is the
Households module's `EraseHousehold` (D-88), run by an operator.

**Other modules may use:** `IPrivacyRequestRepository.ListOpenIdsForHouseholdAsync` (Households: an erasure records
the household's open requests as closed, `privacy.request.closed`, before it deletes them)

**Must not:** Accept free text from participants; include scores, tiers or support recommendations in the export until
their display is approved (D-72); let anyone reach a member they may not act for (D-49)
