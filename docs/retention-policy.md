# Retention policy (placeholder)

> **Status: DRAFT. Requires approval before production.** No retention period for participant data or audit events
> has been approved (open questions 12, 21 and 22 in [decisions.md](decisions.md)). Until a named owner approves this
> document, nothing below is a commitment to families, and the platform keeps participant data and audit events
> rather than delete them on an unapproved schedule. The mechanisms are built but switched off: the retention job
> runs only with `Retention:Enabled=true` and periods that have no defaults, and erasure refuses to run until
> `Erasure:ConsentRecords` is chosen (D-88). This document is not legal advice; the periods must come from the
> programme's legal basis and research protocol.

## 1. Principles

- Keep personal data only as long as the purpose that justified collecting it needs it; do not assume every record
  must be kept forever.
- Withdrawal stops future processing without rewriting history (D-51, D-60): withdrawn consent and withdrawn
  households stay as records, so it remains provable what was allowed when.
- Deleting is an explicit, ordered, audited procedure, never a cascade (D-32).
- Audit events hold no answers or free text: opaque resource IDs, and the actor's identity-provider subject. The
  subject is a persistent pseudonymous identifier, not anonymous: whoever can look it up in the identity provider can
  tell who acted. Keeping audit events after an erasure therefore keeps that link, and the approvers must decide
  whether that is acceptable or whether subjects are replaced in the erased household's events.

## 2. Data categories

| Category | Tables or stores | Proposed period | Status | Mechanism |
| --- | --- | --- | --- | --- |
| Household roster | `households`, `family_members`, `household_memberships` | Study duration plus N years | **Needs approval** | Erasure procedure (§4); the retention job erases households withdrawn longer than `Retention:WithdrawnHouseholdDays` |
| Platform users | `platform_users`, `role_snapshots` | While the account is linked to a household or role; then N months | **Needs approval** | Erasure procedure (§4) |
| Consent records | `consent_records`, `consent_policy_versions` | At least as long as the data they permit; possibly longer as evidence (open question 21) | **Needs approval** | Erasure procedure deletes them with the household once `Erasure:ConsentRecords=delete`; keeping them as evidence is not built |
| Privacy requests | `privacy_requests` | While the household takes part, then with the household | **Needs approval** | Erasure procedure (§4) |
| Assessment answers and sessions | `assessment_sessions`, `assessment_responses`, `assessment_assignments` | Study duration plus N years | **Needs approval** | Erasure procedure (§4) |
| Scores, recommendations, referrals | `score_results` (and children), `support_recommendations`, `support_referrals` | Study duration plus N years; referrals may need the partner agreement's period (open question 13) | **Needs approval** | Erasure procedure (§4) |
| Safety alerts | `safety_alerts` | Until household erasure; no period approved (open question 59) | **Needs approval** | Erasure procedure (§4) deletes a household's alerts first. Codes and times only, no text (D-135) |
| Research export files | Export storage | 7 days after completion | **Proposed** (D-60) | Built: maintenance deletes the file and marks the job Expired |
| Export jobs (no data rows) | `export_jobs` | N months | **Needs approval** | Built, off: the retention job deletes failed and expired jobs older than `Retention:FinishedExportJobsDays` |
| Idempotency keys | `idempotency_keys` | 24 hours | Engineering setting (D-54) | Built: maintenance purge |
| Outbox messages | `outbox_messages` | Processed: 30 days | **Proposed** (D-32) | Built, off: the retention job deletes processed messages older than `Retention:ProcessedOutboxDays` |
| Audit events | `audit_events` | N years (security and research-governance evidence) | **Needs approval** | Retention job not built; the database refuses deletes unless the transaction sets `azwah.audit_retention = 'on'` (D-81) |
| Application logs and traces | Hosting platform | 30 to 90 days | **Needs approval** with hosting (open question 6) | Hosting platform |
| Database backups | Hosting platform | Follows the longest period above, encrypted | **Needs approval** with hosting | Hosting platform |
| On-device data | Phone SQLite and keystore | Until sign-out, a different account signs in, or the app is removed | Built (D-70, D-83) | App |
| Development identity provider | Process memory | Until the process stops | Built (development only) | — |

"N" marks a number only the approvers can set.

## 3. Rights requests (access, correction, erasure)

| Request | Today | Gap |
| --- | --- | --- |
| Access | "Download a copy of my data" in the app (`GET /api/v1/me/data-export`, D-89): the household, the members the caller may act for, consents, assignments, sessions with every answer, referrals and privacy requests. An access request can also be recorded for staff | Scores and recommendations are left out until their display is approved (D-72); response times are open (39) |
| Correction | The household manager corrects roster fields (`PATCH …/members/{id}`), with the rules of D-50 and D-58; a correction request can be recorded for staff | Answers cannot be corrected after submission, by design |
| Withdrawal | Consent per purpose; withdrawing the household freezes the roster | What household withdrawal must stop is open question 25 |
| Erasure | An erasure request is recorded in the app and listed for program administrators (D-89); an operator runs `db erase-household` (§4), which records the household's open requests as closed in the audit trail and deletes them with it | Needs the consent-record rule (21) and the workflow's owner, response times and legal basis (39) |

## 4. Erasure procedure (built, D-88)

`EraseHousehold` runs one transaction that sets `SET LOCAL azwah.erasure = 'on'` and deletes, in order: safety alerts (by the household, by its sessions or by the accounts being erased, which are locked first); support
recommendations; referrals; score results (their contributions and category scores cascade); responses; sessions;
assignments; privacy requests; consent records; memberships; members; the household; platform users left without a
membership and their role snapshots; then idempotency keys and outbox rows that name an erased ID. Audit events stay
(opaque resource IDs, plus actor subjects; see §1), and the erasure itself is audited as one event with the household
ID, plus one `privacy.request.closed` event for each of the household's open privacy requests. The household's row is
locked first, so two erasures of one household cannot both run. A failure rolls everything back; the retention job
counts it and goes on with the next household.

It runs only for a Withdrawn household whose reference the operator confirms, and only once
`Erasure:ConsentRecords=delete` is configured; any other value is refused because keeping consent records as evidence
is not built (open question 21). Operators run `db erase-household --household <id> --reference <ref> [--withdraw]`;
the retention job uses the same procedure. Details: [database-conventions.md](database-conventions.md#retention-and-deletion).

## 5. Approvals needed

| Decision | Owner | Open question |
| --- | --- | --- |
| Retention period for each "Needs approval" row | Product owner with legal and research owners | 12, 22 |
| Whether consent records outlive an erasure as evidence | Product owner, legal | 21 |
| Hosting log and backup retention | Product owner, with hosting | 6 |
| Rights-request workflow and response times | Product owner, legal | 39 |

When approved: record the decision in `decisions.md`, set `Erasure:ConsentRecords` and the `Retention:*` periods in
configuration, switch on `Retention:Enabled`, build anything the decision adds (for example, keeping consent evidence)
with tests, and remove the DRAFT banner.
