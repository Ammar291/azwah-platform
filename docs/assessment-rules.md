# Assessment rules

This file records what is known about the two Azwah instruments, where each rule came from, and whether it is approved.
**Nothing here is approved.** The engine runs only approved versions and rule sets, so it assigns and scores nothing
until a named approver acts. Scoring has its own file: [`scoring-specification.md`](scoring-specification.md).

## 1. Two instruments, never combined

| Instrument | Kind | Content in the platform | Status |
| --- | --- | --- | --- |
| Abu Dhabi Family Survey (family-voices modules A, B, C) | `FamilyVoices`, code `abu_dhabi_family_survey` | Draft version 1 from the codebook (36 items) | Draft; not approved |
| Household assessment (4 options, 39 items, 7 areas) | `HouseholdAssessment`, code `household_assessment` | Draft version 1 from the demo's instrument (39 items, 7 sections, English and Arabic), with the brief's scoring as the Proposed rule set `household_assessment_total` v1 | Draft; not approved (section 3) |

The codebook calls itself a "39-question" survey: 3 roster questions plus 36 module items. It is **not** the household
assessment. Their scales, respondents and proposed bands differ, and their results are never combined.

## 2. Family-voices content: the Abu Dhabi Family Survey codebook

**Source.**
- The Abu Dhabi Family Survey codebook (a CSV) and the matching survey manual, received 2026-10-13. They are
  third-party documents and are not included in this repository (D-150).
- Both documents look like AI chat output, so a named research owner must confirm them (open question 30).
- They supersede the nine items of an earlier sample questions document (not included).

**Draft content** (`backend/FamilyResilience.Infrastructure/Content/abu-dhabi-family-survey.v1.json`, verified
against the CSV where it is kept; `CodebookContentTests` pins the structure and fingerprints the copied text):
- **Sections:** A (Q04–Q13, 10 items), B (Q14–Q26, 13), C (Q27–Q39, 13). The section titles are the codebook's module
  names.
- **Items:** question text in English and Arabic, copied verbatim. Every item is required: the manual says every
  question offers 99, so an answer is always possible. The approver must confirm this.
- **Scoring metadata:** every item has direction `Unspecified` and no category, because the sources designate neither.
- **Options:** values 1–5 with the codebook's English labels, then **99 "Prefer not to answer / يفضل عدم الإجابة"**.
  The 99 option is non-substantive: it is stored as 99 but is never a point on the scale.
- **Not imported:** the roster questions Q01–Q03 (open question 29). Q01 and Q02 map to the household roster (members'
  birth month and employment status).

**Arabic.**
- **Complete:** every question and every 99 label.
- **Reused:** Arabic answer labels are taken from the sample questions document only where its English label is identical.
  This covers the ease, burden, frequency, agreement, contact-frequency and grandparent-help scales, plus "Neutral".
  It completes Q04–Q06, Q10, Q14, Q15, Q19–Q21, Q24–Q29 and Q33–Q37.
- **Missing (draft only; approval is refused until it is supplied):**

| Items | Labels without Arabic |
| --- | --- |
| Q07 | No/Low/Moderate/High/Severe Impact |
| Q08, Q12, Q18 | Not Confident At All, Slightly Confident, Confident, Extremely Confident |
| Q09, Q11 | Definitely Not, Probably Not, Uncertain, Probably Yes, Definitely Yes |
| Q13 | No/Low/Moderate/High/Total Reliance |
| Q16 | <30 mins, 30–60 mins, 1–2 hours, 2–3 hours, >3 hours |
| Q17 | Definitely Not, Probably Not, Probably Yes, Definitely Yes |
| Q22, Q38 | Extremely Dissatisfied, Dissatisfied, Satisfied, Extremely Satisfied |
| Q23 | Light Burden, Heavy Burden |
| Q30, Q31 | Completely Dependent, High Assistance Needed, Moderate Assistance, Minimal Assistance, Completely Independent |
| Q32 | Not Comfortable At All, Slightly Comfortable, Comfortable, Extremely Comfortable |
| Q39 | Constantly/Frequently/Moderately/Seldom Worried, Not Worried At All |
| Sections A, B, C; instrument | Section titles; instrument name and description |

Some items therefore have Arabic on only part of their scale. Q23 has Arabic for "No Burden", "Moderate Burden" and
"Severe Burden" only. Q08, Q12, Q17, Q18, Q22, Q32 and Q38 have Arabic only for "Neutral" and the 99 label. Approval stays
blocked by the domain check and the database trigger until every label has Arabic. Section titles and the instrument
name and description have no Arabic yet.

**Source defects kept verbatim for the content owner** (open question 31):
- Q19's Arabic contains the English word "in" ("…جداول العمل in حدوث توتر");
- Q23's Arabic has "المتمتطلبات".

## 3. Household assessment (brief)

| Property | Value | Status |
| --- | --- | --- |
| Source | The original product brief, as drafted by the demo team in the earlier investor demo's questionnaire (English and Arabic) and answer options; the demo is not included in this repository (D-150) | **Unapproved draft** (open question 1) |
| Content in the platform | `backend/FamilyResilience.Infrastructure/Content/household-assessment.v1.json`, imported by the development seed as draft version 1 of `household_assessment`; it was verified verbatim against the demo's files, and `HouseholdAssessmentContentTests` pins its structure and fingerprints its text | Draft; not approved |
| Items, scale | 39 items, 4 options (1 Strongly Disagree … 4 Strongly Agree), seven sections coded by area (`communication`, `connectedness`, `outlook`, `extended_family`, `wealth_debt`, `seniors`, `work_life`), every item required | Drafted by the demo team |
| Directions and categories | 10 reverse-scored items (Q04, Q07, Q11, Q16, Q21, Q25, Q26, Q32, Q36, Q38); every item's scoring category is its section | Proposed, not approved |
| Reverse scoring, total, tiers | 5 − raw; 39–156; `vulnerable` 39–78, `transitional` 79–117, `flourishing` 118–156, as the Proposed rule set `household_assessment_total` v1 (`household-assessment.scoring.json`; `scoring-specification.md` section 7) | **Proposed, not approved** (open questions 1, 2) |

Not imported: the demo's per-item risk "flags" and its one-sentence area descriptions, which the platform's content
model has no field for. The instrument description is the demo's own sentence ("39 statements across seven areas of
family life.") in both languages.

## 4. Eligibility

**Model.**
- An approved eligibility rule set holds a strict `eligibility.v1` definition. Its target is one module for family
  voices, or the whole version for the household assessment.
- Rule nodes: `ageAtLeast`, `ageAtMost`, `employmentStatusIn`, `isHeadOfHousehold`, `isPrimaryIncomeEarner`,
  `householdHasMemberYoungerThan` (active members only), `all`, `any`.
- Ages are completed years on the evaluation date (UTC). Only the birth month is stored, so for rule thresholds
  (`ageAtLeast`, `ageAtMost`, `householdHasMemberYoungerThan`) the birthday counts as the first of the birth month.
  A member can therefore reach a threshold up to a month early (open question 62).

**Missing information.**
- A missing employment status makes a rule unknown.
- In `all`, a false rule decides the result; in `any`, a true rule does.
- Otherwise the outcome is **indeterminate**, with `missingInformation: ["employment_status"]`. It is never guessed.

**Platform guards** run first: an inactive member, or a member who is not certainly 18, is ineligible. The adults-only
guard does not use the first-of-month convention: a member counts as an adult from the first day of the month after
their birth month, 18 years on, so nobody aged 17 is assigned, asked or able to give consent (D-58, D-139). The same
guard applies to consent, to the manager at enrolment and to linking an account.

**Reason codes** (staff-only):
- `eligible`;
- `member_inactive`, `member_under_18`;
- `age_below_minimum`, `age_above_maximum`;
- `employment_status_not_qualifying`;
- `not_head_of_household`, `not_primary_income_earner`;
- `no_household_member_under_age`;
- `none_of_alternatives_met`;
- `missing_information`.

**Proposed rules.** These come from the sources and are not approved. They are seeded in development as Proposed only,
from `backend/FamilyResilience.Infrastructure/Content/`.

| Rule set | Target | Rule | Source |
| --- | --- | --- | --- |
| `family_voices_module_a` v1 | A | head of household **or** primary income earner | sample-questions Step 2; codebook target "Head of Household / Main Earner" (open question 16) |
| `family_voices_module_b` v1 | B | age 25–55 **and** employment status in [`employed`] **and** an active household member under 18 | sample-questions Step 2; codebook "Working Parents (25–55)" |
| `family_voices_module_c` v1 | C | age 60 or over | sample-questions Step 2; codebook "Senior Citizens (60+)" |

**Employment statuses.**
- The codebook lists Employed, Retired, Unemployed, Student and 99.
- The platform's proposed list adds SelfEmployed, Homemaker and Other (open question 18).
- "Employed" means `employed` only, until open question 26 decides whether `self_employed` counts.

## 5. Assignments

`POST /api/v1/households/{id}/assign` (staff) evaluates every member against every approved eligibility rule set in
effect, and changes assignments in one transaction:

| Decision | Existing assignment for the same instrument and module in the wave | Action |
| --- | --- | --- |
| Eligible | None | Create (reason `reissue` if a cancelled or expired one exists); it expires when the wave closes |
| Eligible | Assigned, in progress or completed, under any version | Keep (no duplicate) |
| Ineligible | Assigned, not started | Cancel |
| Ineligible | In progress or completed | Keep |
| Indeterminate | Any | Keep; create nothing |

A member eligible for several modules gets each module in one run. A withdrawn household is refused (open question 25).
Assignments are never edited: each keeps its exact eligibility rule-set row, and cancelled rows stay as history.

**Concurrency and audit.** Runs for one household are serialised by a row lock on the household (D-44): a second run
waits, then reads the first run's assignments and creates nothing. Every run stages the
`assignments.eligibility.evaluated` audit event in the same commit, even when it creates and cancels nothing, because the
run returns the same per-member reason codes as the preview, which is a read of sensitive data.

`POST /api/v1/households/{id}/evaluate-eligibility` shows the same decisions and plan without changing anything. It takes
no lock.

**Roster confirmation (D-50).** A household manager confirms the roster
(`POST /api/v1/households/{householdId}/confirm-roster`) before anything is assigned to its members. The product owner
approved these completeness rules on 2026-10-14 (D-58):

| Rule | Error (409) |
| --- | --- |
| The household is active | `households.not_active` |
| Exactly one active head of household | `households.roster_has_no_head` |
| Every active member other than the head has a relationship | `households.roster_missing_relationship` |
| Every active member aged 18 or over has an employment status | `households.roster_missing_employment_status` |

Inactive members do not count. Any roster change (adding, updating or deactivating a member) clears the confirmation.
Confirming locks the household, commits the confirmation, then runs the same assignment service as the staff endpoint
for the current wave, in one transaction. The current wave is the single Open wave whose window contains now: none
assigns nothing, and several return `assignments.wave_ambiguous`. The response lists the live assignments without
reason codes.

## 6. Sessions

- **Lifecycle:**
  - Assigned → InProgress → Submitted → Scored;
  - InProgress → Abandoned returns the assignment to Assigned;
  - a submitted session never returns to InProgress.
- **Expiry (D-139):** an assignment expires when its wave closes. After that, starting, saving answers and submitting
  are refused with 409 `assignments.assignment_expired`. The maintenance service marks overdue assignments Expired;
  a session still in progress on one is abandoned first. Nothing answered after the wave closed is scored or
  exported under that wave.
- **Removing a member (D-139):** deactivating a member abandons their sessions in progress and cancels their open
  assignments in the same transaction. What they already submitted is kept.
- **Repeated submission:** submitting again changes nothing and reports `already_submitted`.
- **Retakes and reassessments:** each is a new session. Submitted answers never change (database trigger).
- **Participant endpoints** decide access from the caller's membership (D-47 to D-49). Starting, answering and
  submitting need active participation consent (D-51).
- **Submission** scores the session with the single approved scoring rule set for its scope and routes it (section 7)
  in the same transaction (D-52). With no approved scoring rule set the session stays Submitted and its result is
  pending.

## 7. Support routing (`referral.v1`)

An approved referral rule set routes complete scores to support categories (D-53). Its definition is strict
`referral.v1`, validated when proposed and when approved:

```json
{"schema":"referral.v1","target":{"sectionCode":"A"},
 "routes":[{"categoryCode":"example_category","when":{"tierCodeIn":["example_tier"]}}]}
```

- `target` is one module (`{"sectionCode":"A"}`) or the whole version (`null`); a score is routed only by rule sets
  for its own scope.
- 1 to 50 routes. Category and tier codes are snake case; a category appears in one route only; each route needs at
  least one tier code. Unknown or repeated keys are refused (`referrals.definition_invalid`).
- Tier codes come only from the approved scoring rule set. The definition adds no threshold of its own.

**Routing a score.**
- Only a complete result with a tier routes; an incomplete or untiered result routes nowhere, and is never guessed at.
- Every route whose `tierCodeIn` holds the result's tier contributes its category, in route order.
- Each routed category is stored as a support recommendation (once per score result and category).
- A recommendation becomes a referral only when the member's referral consent is active, read `FOR SHARE` so a
  concurrent withdrawal waits. Withdrawing referral consent later cancels the open and in-progress referrals that
  relied on it (D-51).
- No approved referral rule set for the scope routes nothing; two return `referrals.ambiguous_rules` and roll the whole
  submission back.
- A session scored later by staff is routed the same way.

A support coordinator may hand a referral to a registered partner organisation that covers its category; the
hand-off is manual and the platform sends the partner nothing (D-61, open questions 13 and 33). The codes in the
example are placeholders, not proposed categories or tiers; no referral rule set is approved anywhere outside test
databases.

## 8. Approval process

1. A named domain owner reviews:
   - the version, including the Arabic;
   - the rule sets;
   - the sources (open question 30).
2. Content changes go into a draft:
   - import a corrected content document as the next draft version (`POST /api/v1/instruments/{id}/versions`). Item
     text, Arabic labels, section titles and scoring directions change only this way: copying a draft
     (`POST /api/v1/instrument-versions/{id}/drafts`) duplicates the content as it is, and no endpoint edits an item;
   - complete the instrument's Arabic name and description (`PUT /api/v1/instruments/{id}/text`).
3. Approve the version (`POST /api/v1/instrument-versions/{id}/approve`). This records the approver (the caller), the
   time, the source document and the effective window.
4. Propose rule sets (`POST /api/v1/instrument-versions/{id}/rule-sets`) and approve them
   (`POST /api/v1/rule-sets/{id}/approve`). Definitions are validated at both steps. A mistaken eligibility approval is
   withdrawn by retiring the rule set (`POST /api/v1/rule-sets/{id}/retire`).
5. Approved versions and rule sets never change. A change is a new version. Retiring a version stops new assignments;
   existing records keep pointing at it.

## 9. What stays disabled

- **Eligibility and assignment:** off until the A, B and C rule sets are approved and a version is approved
  (open questions 16, 26, 30, 31).
- **Support routing:** off until a referral rule set is approved.
- **Scoring:** see [`scoring-specification.md`](scoring-specification.md#8-disabled-scoring-rules).
