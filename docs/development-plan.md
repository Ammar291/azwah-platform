# Development plan

**Domain and persistence foundation (done).** The
domain model for every module, and the PostgreSQL schema, repositories, transactions and outbox for households,
members, consent, questionnaires, assignments and sessions, exist ahead of phases 2–6. Those phases now add use cases,
authorisation, endpoints and screens on top. Support referral persistence followed (D-45): the table, repository and
database guard exist; the partner registry and manual hand-offs followed (D-61); phase 8 adds approved referral
rules and the coordinator and participant screens.

**Assessment engine (done, dormant).** The
backend side of phases 4 to 7 exists: content import and approval, eligibility and assignment, the session lifecycle,
and scoring with persisted results. It is dormant because nothing is approved: the family-voices codebook is a draft with
Arabic gaps, the module eligibility rule sets are proposed, and the only scoring rule set (the household assessment's,
D-99) is proposed, not approved. What remains: mobile and
admin screens, and everything that needs approved content. See [`assessment-rules.md`](assessment-rules.md) and
[`scoring-specification.md`](scoring-specification.md).

**Participant and admin API (done).**
- **Built (D-47 to D-57):**
  - platform users and household memberships, with record-based authorization;
  - households and members, roster confirmation with assignment, and consent;
  - participant questionnaires, assignments and sessions, where submission scores and routes in one transaction;
  - support recommendations and referrals;
  - program monitoring, and the analytics overview with suppression and score distributions (D-59);
  - idempotency keys, the outbox dispatcher and the maintenance service;
  - research exports with per-export pseudonyms (D-60), with local file storage for development only;
  - partner organisations and manual referral hand-offs by support coordinators (D-61).
- **Still open:**
  - joining an existing household;
  - a production identity provider; the development identity provider and mobile sign-in are built (D-82, D-83).

**Admin dashboard (done).**
- **Built (D-73 to D-75):** OIDC sign-in with PKCE through a backend-for-frontend, development sign-in and mock data;
  role-based navigation and page guards from `GET /api/v1/me/permissions`; the overview, assessment and participation
  monitoring, resilience analytics, research exports and read-only questionnaires and rules; the admin reads they need
  (D-74).
- **Still open:** the support-coordinator referral queue and partner
  screens; a shared session store before running several instances; study cohorts, version comparability and
  demographic score comparisons (open questions 34 to 36).

**Security hardening (done).**
- **Built (D-76 to D-85):** strict token validation and a start-up guard for deployed settings; TLS, security headers,
  request limits and rate limiting; export quotas; sign-in records, audited refusals and observed role changes;
  append-only audit against DELETE and TRUNCATE; route-sweeping authorization tests; the development identity provider
  (phase 1); mobile OpenID Connect sign-in (phase 1); admin revocation at sign-out; `threat-model.md`,
  `retention-policy.md` and the four-class data model in `security-model.md`.
- **Follow-ups built (D-86 to D-91):** Separate database roles;
  dependency scanning with a dated allowlist and Dependabot; household erasure and a
  retention job, both off until approved; the family data download, privacy requests and the dashboard's
  `/privacy-requests`; security counters with Prometheus alert rules; the app's inactivity lock and app-switcher cover.
- **Still open:** a production identity provider (open questions 5, 37); trying mobile sign-in and the lock on a
  device; approved retention periods and the consent-record rule (21, 22); the rights-request workflow's owner and
  response times (39); the before-production list in [`threat-model.md`](threat-model.md#6-before-production).

**Support-resource discovery (done; directory empty, language model dormant).** As built in [`support-resource-discovery.md`](support-resource-discovery.md).
- **Built (D-92 to D-95):**
  - the `SupportResources` module, isolated from participant data: a versioned, bilingual directory with approval,
    review dates, retirement, database immutability and audit, kept by system administrators;
  - deterministic PostgreSQL full-text search with Arabic normalisation, access filters before matching and
    deterministic ranking; `POST /api/v1/support-resources/search` and the reader endpoint for participants, support
    coordinators and system administrators; search text is never stored or logged, and searches are not audited;
  - the language-model path up to its safety boundary (port, fixed selection prompt, redactor, strict guard, timeouts,
    retries, fallbacks, settings, start-up refusal, metrics), tested against scripted models only; no provider adapter;
  - a synthetic evaluation with retrieval regression floors and a guard evaluation.
- **Still open:**
  - an owner, real entries and a review cadence for the directory (open question 41);
  - mobile and dashboard screens, with reviewed wording for the information-only notice (43);
  - weaker Arabic recall (R@5 0.575 against 0.850 English on the synthetic set): Arabic stemming or the
    semantic-retrieval rule waits for the real directory and an approved query set (D-95);
  - any AI-assisted discovery: every prerequisite in D-94, starting with an approved use case and provider (42);
  - alert rules for the `FamilyResilience.SupportResources` meter.

The foundation (repository, API skeleton, mobile and admin shells, Compose, CI, documentation) is done. Each phase below
gets its own design and plan before any code (kept with the project's history, not in this repository), and ends
with all checks in `CONTRIBUTING.md` passing. Numbers in "Depends on" refer to the open questions in [`decisions.md`](decisions.md).

| # | Phase | Goal | Main deliverables | Depends on | Done when |
| --- | --- | --- | --- | --- | --- |
| 1 | Identity and access | People sign in on mobile and admin | Development OIDC issuer in Compose; mobile sign-in (authorization code + PKCE, secure token storage); admin sign-in with server-held tokens; `users` table mapping subjects to roles; UAE PASS staging through the Azwah identity provider (D-129); production UAE PASS at onboarding | 5, 11, 14 | A signed-in participant and researcher reach `/api/v1/me` from each app; tokens never in logs or browser storage |
| 2 | Households and members | A household can be created and managed by its own people only | Households and members APIs; resource-based authorization; admin read views | 11 | Integration tests prove a caller cannot read or change another household (404) |
| 3 | Consent | Members grant and withdraw consent per purpose | Versioned consent documents (ar, en); grant and withdraw APIs; consent screens | 9, 10 | Every later feature can ask "does member M have consent for purpose P now?" |
| 4 | Questionnaire management | Approved instrument content lives in the platform | Instrument versions with Draft/Approved/Retired; import of approved content; approval workflow and review screens in admin | 1, 2, 3, 4, 16 | An approved version is immutable; unapproved content cannot be assigned |
| 5 | Assignments and eligibility | The right member gets the right instrument in each wave | Server-side evaluation of approved eligibility rules; waves | 16 | Routing matches the approved rules for every member type in tests |
| 6 | Sessions and responses | Members answer on their phones | Session API; mobile answer flow with `LikertScale`; offline drafts after the encryption decision | 7 | Answers validate against the version; submitted sessions are read-only |
| 7 | Scoring | Results from approved rules only | Deterministic engine per instrument; score results with rule-set version; results screens | 1–4 | Golden tests per rule set; unapproved rule sets refuse to run |
| 8 | Support referrals | Consented hand-offs to partners | Partner registry; referral lifecycle; coordinator views | 13 | Referral impossible without current referral consent; every step audited |
| 9 | Research analytics | Researchers see de-identified trends | Aggregated read models; Recharts dashboards; export controls | 12 | No direct identifiers; minimum cell size enforced |
| 10 | Production readiness | Safe to run with real families | Hosting in the agreed region; EAS builds for iOS and Android; monitoring; backups; security testing; migration bundles in the pipeline | 6, 8, 15, 17 | Security review signed off; restore drill passed |

## Next recommended prompts

### Phase 1: identity and access

> Read `CONTRIBUTING.md`, `docs/security-model.md`, `docs/decisions.md` (D-6, D-7, D-17, open questions 5, 11, 14) and
> `docs/architecture.md`. Design sign-in for the Azwah mobile app and admin dashboard against a development OpenID
> Connect issuer running in Docker Compose, keeping UAE PASS as the future production issuer (it is not configured; do
> not pretend otherwise). Cover: the issuer choice, the mobile flow (authorization code with PKCE, secure token
> storage), the admin backend-for-frontend, a `users` table that maps token subjects to platform roles, and how
> `ICurrentUser` gets roles from our records. Brainstorm first, then write the spec and plan. Done when a participant
> and a researcher can sign in from each app and `GET /api/v1/me` returns their roles, with integration tests for 401
> and 403 and no tokens in logs.

### Phase 2: households and members

> Read `CONTRIBUTING.md`, `docs/domain-model.md` (Household, FamilyMember, User), `docs/api-conventions.md` and the
> Households and Members module READMEs. Design and build the households and members API with resource-based
> authorization: a caller sees only households they belong to (others get 404), main-earner rules from the domain
> model, validation with FluentValidation, audit events for every change, EF Core migrations, and admin read views.
> Brainstorm first, then spec and plan. Done when integration tests prove cross-household access is impossible and all
> checks in `CONTRIBUTING.md` pass.

### Phase 3: consent

> Read `CONTRIBUTING.md`, `docs/domain-model.md` (ConsentDocumentVersion, Consent), `docs/security-model.md` and open
> questions 9 and 10. Design versioned consent documents in Arabic and English, per-purpose grants and withdrawals
> (participation, research use, referral), the consent screens in the mobile app (right to left in Arabic) and the
> query other modules use to check consent. Do not write legal consent text: use clearly marked placeholders until the
> reviewed text arrives. Brainstorm first, then spec and plan. Done when consent can be granted and withdrawn, every
> change is audited, and later modules can check consent through one Application interface.
