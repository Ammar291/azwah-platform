# Decisions

Decision records are append-only. To change a decision, add a new record that supersedes it and mark the old one
"Superseded by D-n". Open questions that block later phases are in the register at the end.

This public repository is a snapshot of the application (D-150). Earlier records name files that are not in it: design
records and plans under `docs/superpowers/`, evaluation notes under `docs/evaluations/`, design mockups under
`docs/design/`, the third-party source documents under `docs/sources/`, and the earlier investor demo `apps/demo-web`.
They stay in the project's private history.

## D-1 Modular monolith in Clean Architecture

- **Context:** One team, one database, many related modules; the brief forbids microservices without a concrete need.
- **Decision:** Six .NET projects (Domain, Application, Infrastructure, Api, UnitTests, IntegrationTests); modules are
  folders inside each layer.
- **Consequences:** Simple deployment and transactions; module boundaries rely on discipline and the module READMEs;
  `DependencyRulesTests` enforces the layer rules.

## D-2 Repository layout and the demo

- **Context:** The repository held the investor demo at its root.
- **Decision:** The demo moved to `apps/demo-web` with its history; the root holds only the platform; `render.yaml`
  stays at the root with `rootDir: apps/demo-web`; personal files live in the ignored `/materials`.
- **Consequences:** The demo keeps deploying unchanged; the platform does not import from it.

## D-3 Independent npm projects

- **Context:** Expo SDK 57 pins React 19.2.3; Next.js 16.4 uses React 19.3.0.
- **Decision:** Each JavaScript app has its own `package.json` and lockfile; no workspaces; a root `package.json` holds
  convenience scripts only.
- **Consequences:** No React version clashes; small duplications (health status logic) are accepted and documented.

## D-4 .NET SDK pin

- **Context:** The machine has SDK 10.0.401; the SDK container image ships the 1xx feature band.
- **Decision:** `backend/global.json` sets `10.0.100` with `rollForward: latestFeature` and selects the Microsoft
  Testing Platform runner.
- **Consequences:** Local, container and CI builds all resolve a .NET 10 SDK.

## D-5 Tests: xUnit v3 and real PostgreSQL

- **Decision:** xUnit v3 on Microsoft Testing Platform; integration tests use `WebApplicationFactory` against
  PostgreSQL 18.6 started by Testcontainers. Test settings are injected with `ConfigureAppConfiguration` so they win over
  user secrets and environment variables.
- **Consequences:** Docker is required to run integration tests; no in-memory database surprises.

## D-6 Authentication

- **Decision:** JWT bearer tokens from an OpenID Connect issuer, configured from `Authentication:Schemes:Bearer`;
  inbound claim mapping off (`sub`, `role`); `dotnet user-jwts` for development. UAE PASS is the target provider and
  is not configured.
- **Consequences:** No custom token code; switching issuers is configuration only.

## D-7 Authorization

Superseded by D-48.

- **Decision:** An authenticated-by-default fallback policy; named role policies (`Participant`, `Researcher`,
  `SupportCoordinator`, `Administrator`); resource checks in handlers from the households phase.
- **Consequences:** Forgetting an attribute fails closed. The role list is proposed (open question 14).

## D-8 Errors

- **Decision:** RFC 9457 ProblemDetails for every error; a global exception handler returns a generic 500 with a trace
  ID; status code pages give empty 4xx responses a body.
- **Consequences:** Clients get one error shape; internal details stay in logs.

## D-9 Validation

- **Decision:** FluentValidation validators registered in DI, run by an MVC action filter before each action; error
  keys are camelCase property paths.
- **Consequences:** Controllers stay thin; every new request needs a validator.

## D-10 Persistence conventions

- **Decision:** EF Core with Npgsql; snake_case names (`EFCore.NamingConventions`); `timestamptz` in UTC; UUID v7 keys;
  enums stored as text.
- **Consequences:** Readable SQL for researchers and operators; time-ordered keys.

## D-11 Migrations

- **Decision:** Migrations live in Infrastructure, created through a design-time factory; they run on startup only when
  `Database:ApplyMigrationsOnStartup` is true (development, tests); production applies a migration bundle first.
- **Consequences:** No schema changes race with application start in production.

## D-12 Observability

- **Decision:** Built-in JSON console logging and OpenTelemetry (traces, metrics, logs) for ASP.NET Core, HttpClient,
  the runtime and Npgsql (`Npgsql.OpenTelemetry`; the EF Core instrumentation has no stable release). Export only when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Health endpoints are excluded from traces.
- **Consequences:** No Serilog dependency; any OTLP collector works.

## D-13 Small dependencies

- **Decision:** .NET `TimeProvider` instead of a custom clock; no MediatR (commercial licence since 2025); plain xUnit
  assertions (FluentAssertions changed licence too).
- **Consequences:** Fewer third-party packages.

## D-14 Mobile foundation

- **Decision:** Expo `blank-typescript` template plus Expo Router; routes in `app/`, code in `src/`; Expo Go for quick
  development; development builds for anything native (RTL layout, encryption); EAS for store builds.
- **Consequences:** The app config (`app.json`) and config plugins describe native behaviour; `ios/` and `android/` are
  generated and ignored.

## D-15 Mobile right-to-left

- **Decision:** Choosing Arabic calls `I18nManager.forceRTL` and reloads; the language reloaded for is stored first,
  and a reload that does not change the direction is not repeated. Layout uses start/end styles only (lint rule).
- **Consequences:** Verified on Android in a development build. **Expo Go (SDK 57) cannot show RTL layout**: it resets
  the direction for every project, so Arabic shows left to right there. The expo-localization plugin is used without
  `supportsRTL`/`forcesRTL`: with either set, its iOS module calls `forceRTL(device direction)` on every launch and
  would undo the in-app choice (found in the final review; a project-config test guards it). iOS behaviour still
  needs checking on an iPhone development build (open question 8).
- **Correction (2026-10-28):** Expo Go also ignores `reloadAppAsync` (it resolves; nothing reloads). The switch,
  which ignored every request after asking for a reload, then left people stuck in the language they had just chosen.
  Now no reload is asked for in Expo Go (`expo-constants` reports `storeClient`), and anywhere else a switch that is
  still running 5 seconds after asking for a reload drops the reload's app-lock mark and accepts requests again.

## D-16 Mobile storage

- **Decision:** Expo SQLite with migrations tracked by `PRAGMA user_version`; a `settings` table; an empty `drafts`
  table until on-device encryption is decided.
- **Consequences:** No participant data on devices yet.

## D-17 Admin dashboard

- **Decision:** Next.js 16.4 without Cache Components (every page is per-user and dynamic); shadcn/ui with RTL support;
  language in a cookie with `lang`/`dir` on `<html>`. Sign-in planned as OIDC authorization code with PKCE and tokens
  held on the server (backend-for-frontend). Tests run on Vitest without `@vitejs/plugin-react` (its optional Babel 8
  peers conflict with shadcn's Babel 7; Vite 8 compiles TSX itself).
- **Consequences:** No tokens in browser storage once sign-in arrives.

## D-18 Health contract

- **Decision:** `/health/live` (process) and `/health/ready` (database); only `{"status": …}` is returned; 503 means not
  ready. Both clients map this to ready, not ready, unreachable or not configured, with a 5-second timeout.
- **Consequences:** The status logic exists in both clients by design (D-3).

## D-19 Local infrastructure

- **Decision:** Docker Compose for development only (PostgreSQL 18.6 and the API); the development API listens on all
  interfaces so phones on the same Wi-Fi can reach it; ports are configurable in `.env`.
- **Consequences:** Never expose the development setup beyond a trusted network.

## D-20 No questionnaire content yet

- **Decision:** No items, options, scoring rules or eligibility rules are seeded. The demo's content is recorded as an
  unapproved draft in `assessment-rules.md`.
- **Consequences:** The questionnaire phase cannot finish until content is approved (open questions 1–4).

## D-21 Windows development constraints

- **Context:** Native Android builds embed full paths in C++ object names and hit Windows' 260-character limit from
  deep folders; `subst` drives and junctions fail because Node resolves them back to the long path.
- **Decision:** Build native apps from a short clone path; documented in `apps/mobile/README.md` and `CONTRIBUTING.md`.
- **Consequences:** Developers on Windows keep the repository near the drive root.

## D-22 Domain and persistence foundation ahead of the feature phases

- **Context:** Phases 2–8 each planned their own entities; the persistence rules (keys, constraints, concurrency,
  transactions, outbox) are cheaper to set once.
- **Decision:** Build the domain model for every module and the PostgreSQL schema for households, members, consent,
  questionnaires, assignments and sessions now, without endpoints or use-case handlers. Spec
  `docs/superpowers/specs/2026-10-11-domain-persistence-design.md`.
- **Consequences:** Feature phases add use cases, authorisation, endpoints and screens on an existing model. Score
  result and referral persistence remains (open question 23).

## D-23 Aggregates and module ownership

- **Decision:** `Household` owns its members (Members module types, changed only through the household).
  `InstrumentVersion` owns sections, items and options. `AssessmentSession` owns responses. Every other entity is its
  own aggregate. One repository per aggregate root; no generic repository.
- **Consequences:** Roster rules are checked in one place; children of an aggregate never change on their own.

## D-24 Year and month of birth; children on the roster

- **Decision:** Members store year and month of birth only. Children are ordinary roster members, never assigned an
  assessment until assent rules are approved. Child presence is derived from the roster.
- **Consequences:** Ages are exact to the month at every wave without storing a full date of birth.

## D-25 Integrity in the database as well as the domain

- **Decision:** Restrictive foreign keys, composite foreign keys that pin copied keys, partial unique indexes, generated
  CHECK constraints for every enum, an exclusion constraint on approved version windows, and triggers that keep
  approved content, submitted answers, session copies and audit events from changing.
- **Consequences:** Normal `INSERT`, `UPDATE` and `DELETE` statements from the application role cannot change approved
  content, submitted answers, session copies or audit events. Table owners, `TRUNCATE` and
  `session_replication_role = replica` bypass triggers, so production must not run the application as the table owner.
  Raw SQL against approved content fails by design; answering and submitting are separate commits.

## D-26 Optimistic concurrency with `xmin`

- **Decision:** Every aggregate root maps PostgreSQL's `xmin` as its row version; child changes touch the root.
- **Consequences:** No version column to maintain; a stale update returns `persistence.concurrency_conflict`.

## D-27 Unit of work and expected conflicts

- **Decision:** `IUnitOfWork.CommitAsync` and `ExecuteInTransactionAsync` (through the execution strategy, not
  reentrant). Concurrency conflicts and known unique or exclusion violations return `Result` errors; other database
  errors throw. Audit events are staged with `IAuditLog.Stage`.
- **Consequences:** One use case is one transaction, including its audit and outbox rows.

## D-28 Outbox with ID-only events

- **Decision:** Domain events with stable type codes become `outbox_messages` rows in the same save. Payloads carry IDs,
  enums, numbers and times only (an architecture test enforces this). No dispatcher until a consumer exists.
- **Consequences:** Nothing is published yet; rows wait in `Pending`.

## D-29 One rule-set table; separate, immutable score results

- **Decision:** Scoring, eligibility and referral rule sets share `rule_sets` (kind, code, version, approval, `jsonb`
  definition). Score results are separate, immutable rows that record the rule set used.
- **Consequences:** Every reference to "rules" is a real foreign key to an approved row; re-scoring never overwrites.

## D-30 Two migrations for this foundation

- **Decision:** `AddCoreDomainModel` is generated from the model; `AddDatabaseGuards` holds the hand-written SQL.
- **Consequences:** Regenerating the model migration while the branch was unmerged never lost hand-written SQL.

## D-31 Synthetic development seed

- **Decision:** `db seed` adds nameless synthetic households, a planned wave, draft consent policies and one draft
  `[DEV SAMPLE]` instrument. Nothing approved. Development environment only.
- **Consequences:** No real or realistic participant data, and no unapproved content activated.

## D-32 Retention and deletion strategy (proposed)

- **Decision:** Deactivate rather than delete; erasure is an explicit ordered transaction using `azwah.erasure`;
  processed outbox rows kept 30 days; other periods wait for approval. Details in `database-conventions.md`.
- **Consequences:** Nothing sensitive is deleted by accident; nothing is kept forever once periods are approved.

## D-33 Data-driven eligibility and scoring definitions

- **Context:** Rules must be versioned, approved and testable, and thresholds must not be invented in code.
- **Decision:**
  - Eligibility rule sets hold strict `eligibility.v1` JSON: a closed set of typed specifications.
  - Scoring rule sets hold `scoring.v1` JSON naming a strategy (policy code and algorithm version) and its parameters.
  - Each definition is validated by the module that owns it (Assignments, Scoring), on proposal and again on approval.
- **Consequences:** Thresholds live in approved rule-set rows. Changing a number means a new rule-set version, with no
  release needed. The engine refuses unknown nodes, keys and policies.

## D-34 Three-valued eligibility on the evaluation date

- **Decision:**
  - Missing roster facts give an **indeterminate** outcome, never a guess.
  - Ages are taken on the evaluation date (UTC).
  - Inactive members and members under 18 are never eligible.
- **Consequences:** A member who turns 60 mid-wave qualifies on the next run. Indeterminate members get nothing until
  the fact is recorded.

## D-35 Assignment uniqueness per instrument and module per wave; re-evaluation

- **Decision:**
  - The engine treats a live or completed assignment for the same instrument and module in the wave as satisfying it,
    whatever its version. This answers open question 20 in the engine; the database still enforces uniqueness per
    version.
  - Re-running cancels only not-started assignments of members who no longer qualify.
- **Consequences:** No member answers a module twice in a wave because a new version took effect. Started and
  completed work is never cancelled.

## D-36 Staff-only API until identity exists

Superseded by D-47.

- **Decision:**
  - Assessment-engine endpoints require the Administrator policy.
  - Participant session use cases exist behind `IParticipantAccess`. Its only production implementation,
    `DenyAllParticipantAccess`, refuses everyone.
- **Consequences:** No endpoint lets one participant act on another's session. The identity phase supplies the real
  access rule and the participant endpoints.

## D-37 Idempotent submission

- **Decision:** `AssessmentSession.Submit` returns `Submitted` or `AlreadySubmitted`. A repeated submission changes
  nothing and raises no event.
- **Consequences:** Retried requests are harmless. The `Idempotency-Key` header (api-conventions) is still planned for
  the participant endpoint.

## D-38 Score results persisted, immutable, with a status

- **Decision:**
  - Results are stored in `score_results`, `score_contributions` and `score_category_scores`. They are unique per
    session and rule set, and update-refusing triggers protect them.
  - A result is `Complete` or `Incomplete`, with a reason.
  - Scoring and the session's move to Scored commit together.
- **Consequences:** Closes open question 23 for scores (referrals remain). A failed scoring saves nothing.

## D-39 Non-substantive answer options

- **Decision:** `ResponseOption.IsNonSubstantive` marks answers such as "99 Prefer not to answer". They keep their raw
  value and are never a point on the scale. An item has at most one.
- **Consequences:** The codebook's 99 can be recorded faithfully, and scoring can exclude it under an approved policy.

## D-40 Arabic optional in drafts only

- **Decision:**
  - Draft content and instrument names may lack Arabic.
  - Approval requires bilingual text: domain check plus the `trg_instrument_versions_bilingual_on_approval` trigger.
  - Arabic answer labels are reused only from an identical English label in an earlier source.
- **Consequences:** Incomplete source content can sit in the platform as a draft without invented translations.

## D-41 The codebook is the family-voices instrument

- **Decision:** The Abu Dhabi Family Survey codebook is imported as draft version 1 of the family-voices instrument
  (`abu_dhabi_family_survey`). The household assessment stays a separate instrument with no content.
- **Consequences:** The brief's 39–156 bands can never apply to the modules. Roster questions Q01–Q03 stay with the
  households phase. The development seed also imports the codebook as draft version 1 with the three proposed
  eligibility rule sets, which supersedes D-31's scope.

## D-42 `ScoringDirection.Unspecified`

- **Decision:** Items whose direction is not designated by an approved specification are `Unspecified`. Such a version
  may be published for data collection, but no scoring rule set can cover it.
- **Consequences:** Directions are never guessed. A version must state its directions before its sessions can be
  scored.

## D-43 Error mapping, enum strings and the OpenAPI contract

- **Decision:**
  - `ErrorResults` classifies every error code once: 400 request, 404 not found, 409 state conflict, or 500 for codes
    that only a bug can produce. A test proves every declared code is classified.
  - Enums are serialised as snake-case strings.
  - `backend/openapi/v1.json` is the committed contract, checked by `OpenApiContractTests`.
  - No controller has a class-level `[Produces]`: that attribute rewrote every error to `application/json`.
  - Every error response, including 401 and 403, is declared and returned as `application/problem+json`. Every success
    response is declared as `application/json`. `OpenApiContractTests` asserts these media types for every response in
    the contract.
  - `ValidationFilter` reports camelCase keys for query-bound requests too, for example `page` and `pageSize`.
  - The test host's `TestOnlyController` is excluded from the contract, and the snapshot is written without escaping `+`.
- **Consequences:** Clients get a stable `code` to translate. API changes show up as a reviewed diff.

## D-44 Assignment runs are serialised per household

- **Context:** The live-assignment unique index includes the instrument version. Two overlapping first runs on either
  side of a new version's `effective_from` could each create a live assignment for one module. Opposite insert orders
  could also deadlock, and an unmapped deadlock would surface as a 500.
- **Decision:**
  - `AssignHouseholdForWave` takes `IHouseholdRunLock.LockAsync` as its first database action inside its transaction.
    The Infrastructure implementation `HouseholdRunLock` runs `SELECT id FROM households WHERE id = … FOR UPDATE`.
  - A second run for the same household waits, then reads the first run's committed assignments and creates nothing.
  - Members are planned in id order and targets in (instrument code, section code) order.
  - Every assign run stages the `assignments.eligibility.evaluated` audit event in the same commit, even when it creates
    and cancels nothing. The run returns the same per-member reason codes as the preview, which is a read of sensitive
    data.
- **Consequences:** Runs for one household queue. The lock is held only for the run's transaction, and it blocks member
  inserts for that household during that time, because `FOR UPDATE` blocks `FOR KEY SHARE`. The
  `evaluate-eligibility` preview takes no lock. The lock throws if no transaction is open, on purpose. The unique index
  stays as a backstop.

## D-45 Support referrals persisted, with a database guard

- **Context:** Open question 23. A score and the referral decisions that follow it must commit together, and a referral
  without the member's active referral consent must be impossible even for raw SQL.
- **Decision:**
  - `support_referrals` stores `SupportReferral`, which now copies its session's instrument version. Composite foreign
    keys pin the session's member and version, the referral rule set's version and the consent's member.
  - `ux_support_referrals_live` allows one non-cancelled referral per session and category
    (`referrals.already_referred`).
  - `trg_support_referrals_guarded`, in the hand-written `AddSupportReferralGuards`, applies three rules:
    - **On insert:** the referral is Open, its session is Scored, its rule set is an approved referral rule set, and its
      consent is an active referral consent. The trigger reads the consent `FOR SHARE`.
    - **On update:** the copied keys, category, consent and creation time never change, and the status only moves
      forward. A service is recorded only by the move from Open to InProgress. That move re-reads the consent
      `FOR SHARE` and needs it still active, because assigning a service hands the referral on. A resolved or cancelled
      referral never changes.
    - **On delete:** allowed only when the transaction sets `azwah.erasure`.
  - Scoring and referring are two commits inside one `ExecuteInTransactionAsync`, because the trigger reads the
    session's status and EF Core does not order statements inside one save.
  - The four foreign keys and their indexes are named by hand: generated names pass PostgreSQL's 63-character limit
    and the two keys to `assessment_sessions` would collide.
  - No use case, endpoint, referral rule definition or seed data. Referral rules and partners are not approved
    (open questions 13 and 33).
- **Consequences:**
  - Closes open question 23.
  - A withdrawal that races a referral waits for it, so no referral commits against consent withdrawn meanwhile.
  - A referral left Open against withdrawn consent can never be assigned. The domain's `AssignService` takes no
    consent, so the trigger is the backstop; the future assignment use case must check the consent itself first.
  - Cancelling open referrals after a withdrawal is the job of the future consent-withdrawal use case. Inside one
    `ExecuteInTransactionAsync`, it must withdraw and commit first, then list and cancel the open referrals in a later
    statement. That order sees any referral that committed while the withdrawal waited. The reverse order can miss
    one, or deadlock against a concurrent referral insert.

## D-46 Generic JSON error messages and retiring rule sets

- **Context:** A malformed request body returned a 400 that repeated System.Text.Json's exception message, naming
  internal types (`FamilyResilience.Application.Questionnaires.ContentOptionDto`) or parser positions
  (`Path: $.age | LineNumber: 0`). That broke security rule 5. Separately, a mistaken approval could not be undone: an
  extra approved eligibility rule set makes every assignment run fail with `eligibility.ambiguous_rules`, and an extra
  approved scoring rule set makes scoring fail with `scoring.ambiguous_rules`.
- **Decision:**
  - `JsonOptions.AllowInputFormatterExceptionMessages` is off. Every JSON input error, including the strict content
    import read by `ContentJsonInputFormatter`, returns MVC's generic message keyed by the JSON path (for example
    `$.sections[0].items[0].options[0]`).
  - `POST /api/v1/rule-sets/{ruleSetId}/retire` (Administrator) moves an Approved rule set to Retired. It takes no body;
    `retiredAt` is the server's clock. It writes the audit action `questionnaires.rule_set.retired`. Only Approved rule
    sets can be retired: a Proposed or already retired one returns 409 `questionnaires.rule_set_not_approved`, an unknown
    id 404. The database trigger `azwah_rule_sets_immutable` already allowed Approved to Retired, so there is no
    migration. A retired rule set never runs again; results already recorded keep the rule set they name.
- **Consequences:**
  - Clients cannot learn internal type names or parser positions from a 400.
  - Correcting a mistaken approval means retiring it, then proposing and approving a corrected rule set with a new code
    or version number.

## D-47 Platform users and household memberships

- **Context:** Participant endpoints need to know which household and member a signed-in caller is, from the
  platform's own records rather than from IDs in requests. Spec `docs/superpowers/specs/2026-10-14-participant-api-design.md`.
- **Decision:**
  - `platform_users` maps a token subject to a platform user. It is created when a participant creates a household.
  - `household_memberships`, inside the `Household` aggregate, links a user to the member they are, as `manager` or
    `member`. A user has at most one active membership, and a member at most one (partial unique indexes).
  - The creator of a household is its manager, linked to the roster entry marked `isSelf`, who must be 18 or over.
    The manager's member cannot be deactivated.
  - Roles still come from the validated token (D-6). Joining an existing household is not built.
- **Consequences:** Supersedes D-36: `CallerAccess` replaces `DenyAllParticipantAccess`, and participant endpoints
  exist.

## D-48 Actor policies, resource handlers and hidden resources

- **Decision:**
  - The proposed roles are `participant`, `researcher`, `support_coordinator`, `program_administrator` and
    `system_administrator`; `administrator` is renamed `system_administrator`.
  - Policies: `Participant`, `HouseholdManager`, `AssignmentParticipant`, `SessionParticipant`, `Researcher`,
    `ProgramAdministrator`, `SystemAdministrator`, `SupportCoordinator`, `AnalyticsReader` (researcher or program
    administrator) and `QuestionnaireReader` (any role). Each needs a token subject.
  - The participant policies add resource requirements, checked by `IAuthorizationHandler`s from the caller's
    membership. A failed resource requirement answers 404 with the resource's not-found code
    (`AuthorizationResultHandler`); a failed role answers 403. Use cases check access again.
  - No staff endpoint returns answers. Staff score responses drop per-item contributions (they carry raw answers) and
    give `contributionCount` instead. Program administrators see progress, never answers or scores.
  - `GET /api/v1/me/permissions` evaluates the same policies for display; it grants nothing.
- **Consequences:** Supersedes D-7's role list. Development tokens need the new role names.

## D-49 Who may act for a member (answer to open question 11)

- **Decision:** A caller may act for the member linked to their own membership and, as an active manager, for any
  active member of the same household who has no membership of their own. A member with their own account is never
  reachable by the manager.
- **Consequences:** A household lead can answer and record consent for relatives without an account. Approved by the
  product owner on 2026-10-14 (D-58), closing open question 11. The legal review of the consent text (open question 9)
  still applies.

## D-50 Roster confirmation and the current wave

- **Decision:**
  - Confirming needs an active household, exactly one active head, a relationship for every other active member and an
    employment status for every active adult (approved by the product owner on 2026-10-14, D-58). Any roster change
    clears the confirmation.
  - Confirmation locks the household, commits the confirmation, then runs `HouseholdAssignmentService` for the current
    wave, in one transaction. The current wave is the single Open wave whose window contains now; none assigns nothing
    and several return `assignments.wave_ambiguous`.
  - The response lists the live assignments without eligibility reason codes.
- **Consequences:** The staff `assign` endpoint uses the same service.

## D-51 Consent for participation, research use and referral

- **Decision:**
  - A grant needs the current approved policy of its type, an active household and a member of 18 or over (adults
    only, D-58). Granting the active version again changes nothing; a newer version withdraws the older grant first.
  - Starting a session, saving answers and submitting need active participation consent: 403
    `sessions.participation_consent_required`, the first error code that maps to 403.
  - Withdrawing referral consent cancels the open and in-progress referrals that relied on it, after the withdrawal
    commits (D-45 order), with resolution `consent_withdrawn`.
- **Consequences:** `ErrorResults` gains a Forbidden set.

## D-52 Submission scores and routes in the same transaction

- **Decision:**
  - Submission locks the session row (`ISessionLock`), then commits in order: the submission with its audit event and
    idempotency key; the score from the single approved scoring rule set for the scope; the routing decisions. Any
    failure rolls all of them back, so no session is left Submitted without its result.
  - With no approved scoring rule set the session stays Submitted and its result reads `pending`; staff can score it
    later, and that also routes it.
  - Saving answers takes the same lock in its own transaction, so answers sent during a submission wait and then get
    409 `sessions.not_in_progress`.
- **Consequences:** Closes open question 32: scoring runs automatically at submission.

## D-53 Referral routing rules and support recommendations

- **Decision:**
  - Referral rule sets hold strict `referral.v1` definitions: a target scope and routes from tier codes to
    snake-case support categories. The Referrals module validates them on proposal and approval.
  - Each routed category is stored as a `SupportRecommendation`. It becomes a referral only when the member's referral
    consent is active, read `FOR SHARE`. Two approved referral rule sets for one scope return
    `referrals.ambiguous_rules`.
  - Participants list their recommendations and referrals; no partner organisation or hand-off exists (open
    questions 13 and 33).
- **Consequences:** No referral rule set is approved anywhere outside test databases.

## D-54 Idempotency keys

- **Decision:** Submissions need an `Idempotency-Key` header. `idempotency_keys` stores the subject, key, operation,
  resource ID and a SHA-256 hash of the request for 24 hours, in the same transaction as the change; no response body
  is stored. A retry re-reads the resource; a key reused for another request returns 409
  `requests.idempotency_key_reused`. The maintenance service purges expired keys.
- **Consequences:** A failed request stores no key, so the client can retry with the same key. A request that finds
  the session already submitted stores no key either: only the request that submitted is replayed as `submitted`.

## D-55 Analytics overview and small-cell suppression

- **Decision:**
  - `GET /api/v1/admin/analytics/overview` (researchers and program administrators) returns:
    - enrolment counts;
    - assignment counts by status and the completion rate;
    - completion by version;
    - a monthly or weekly completion trend;
    - completion by age band and employment status.
  - Every count runs in PostgreSQL through fixed queries. The wave and the time window are the only inputs.
  - Demographic cells from 1 to `Analytics:MinimumGroupSize - 1` are suppressed, plus a complementary cell when only
    one would be. The minimum of 10 was approved by the product owner on 2026-10-14 (D-58).
  - Score distributions were not part of the first overview; D-59 adds them.
- **Consequences:** No individual value leaves the overview. Adding tier distributions needs a tier-count query
  limited to approved rule sets and research-use consent.

## D-56 Outbox dispatcher and maintenance

- **Decision:**
  - `OutboxProcessor` claims one due message of a handled type with `FOR UPDATE SKIP LOCKED` and runs its handler in
    its own scope. A failure records a short code and retries after an exponential backoff (at most an hour), up to
    `Outbox:MaxAttempts` (5); then the message is Failed. Types with no handler stay Pending.
  - Two hosted services run it and an hourly maintenance task. `BackgroundJobs:Enabled` turns them off (the test host
    does). No message broker; nothing is sent outside the platform.
  - Research exports were not built in this phase; D-60 adds them.
- **Consequences:** Every event type without a handler stays Pending until its first consumer arrives. The first
  handler is the export worker (`analytics.export_requested.v1`, D-60).

## D-57 Merge patch and camelCase binding errors

- **Decision:**
  - Member `PATCH` follows JSON merge patch through `Optional<T>`: an absent field is unchanged, and `null` clears a
    clearable field.
  - `SuppressModelStateInvalidFilter` is on, so binding errors (for example `?status=unknown`) reach
    `ValidationFilter` and every 400 key is camelCase.
  - A paging query parameter must not be named `page`: MVC would treat the `page` query key as its prefix.
- **Consequences:** OpenAPI documents an `Optional<T>` field as its value, or null where it can be cleared.

## D-58 Product-owner approvals of 2026-10-14

- **Context:** The participant API shipped with four rules marked proposed or interim. The product owner reviewed them
  in this session.
- **Decision:** Approved as they stand:
  - **Accounts (open question 11):** D-49. The household lead answers for members without their own account; a member
    with an account answers only for themselves.
  - **Consent and assent for minors (open question 10):** adults only. Only members aged 18 or over give consent and
    answer; members under 18 are never assigned or asked (the eligibility guard and `consent.member_under_18`). A
    member who holds any active consent cannot be corrected to under 18 (`households.member_has_consent`, 409): their
    consents are withdrawn first. Two simultaneous requests (a grant and such a correction) are not serialised.
  - **Minimum group size (open question 12, in part):** analytics cells covering fewer than 10 people are suppressed
    (`Analytics:MinimumGroupSize`, D-55).
  - **Roster completeness (D-50):** one active head, a relationship for every other active member and an employment
    status for every active adult.
- **Consequences:** Open questions 10 and 11 close. Open question 12 stays open for the rest of research access
  (de-identification rules and retention periods). The legal review of the consent text (open question 9) is still
  required. Nothing here approves questionnaire content or rule sets: those still need a named approver through the
  approval endpoints (`docs/assessment-rules.md`, section 8).

## D-59 Score distributions in the analytics overview

- **Context:** D-55 left score distributions out. The spec asks for tier distributions of complete score results,
  research-use consent only.
- **Decision:**
  - One distribution per scoring rule set and scope (instrument, version, module): the count of complete results in
    each tier, and their total. First part of the overview; moved to a researcher-only endpoint by D-62.
  - Counted in PostgreSQL by one fixed query: complete results with a tier, of scoring rule sets that are still
    Approved, for members whose research-use consent is active now, optionally limited to one wave. Incomplete
    results, retired rule sets and members without research-use consent are left out.
  - A total from 1 to `MinimumGroupSize - 1` suppresses the whole distribution, including which tiers occur (D-62).
    Otherwise tier cells are suppressed like demographic cells, with a complementary cell when only one would be hidden
    (D-55).
  - Only tiers that occur are listed, in code order; the tier codes come from the approved rule sets.
- **Consequences:** No total score or individual tier is returned. Withdrawing research-use consent removes a
  member's result from the next read.

## D-60 Research exports

- **Context:** Spec sections 4.6 and 8; D-56 left exports out. Open question 12 (de-identification rules and
  retention) is still open, so the rules below are engineering proposals.
- **Decision:**
  - Researchers request a CSV export with `POST /api/v1/admin/exports` (`Idempotency-Key` required, 202 with
    `Location`). The dataset is `assessment_sessions` (submitted and scored sessions) or `score_results` (results of
    scoring rule sets that are still Approved). Columns come only from `ExportCatalog`, whose allowlist and value
    projection are one table; the filters are one wave and one instrument code.
  - Only the requester can read an export or download its file (`ExportRequester` policy, 404 for anyone else).
    Downloads are audited and served with `Cache-Control: no-store`.
  - The outbox handler for `analytics.export_requested.v1` writes the file (`ProcessExport`). Each attempt is committed
    before rows are read; after 3 attempts the job fails with `analytics.export_attempts_exhausted`. More than
    `Exports:MaxRows` (50 000) rows fails it with `analytics.export_too_large`. A finished job is never processed again.
  - De-identification (proposed): only members with active research-use consent at processing time; no IDs,
    references, names, exact dates or birth months; people appear as 32-character HMAC-SHA-256 pseudonyms under a
    random key that exists only while one file is written, so rows link within a file and never across files or back
    to the platform; ages are bands and dates are months; rows are ordered by pseudonym, not by enrolment.
  - Files go through `IExportFileStore`. `LocalExportFileStore` (`Exports:LocalDirectory`, an absolute path) is used
    only in Development and the test host; elsewhere exports are unconfigured. It writes a temporary file and moves it
    into place, so the file at an export's key is never partial; deleting a key also removes temporary files a crash
    left behind. Without storage, requests return 409 `analytics.export_storage_not_configured`.
  - Files expire after `Exports:RetentionDays` (7, proposed); the maintenance service deletes them and marks the job
    Expired. A job that fails deletes any file an earlier attempt wrote, and maintenance fails jobs that have not
    moved for `Exports:StalledAfterHours` (24) with `analytics.export_stalled`, deleting their files too. A download
    whose file is already gone answers 409 `analytics.export_expired` and is not audited as a download.
  - Households that have withdrawn from the study are left out of exports and score distributions, even when a
    member's research-use consent is still active (proposed after the branch review; open question 25 covers what
    withdrawal stops).
- **Consequences:** Production exports wait for approved private storage (open question 6) and for the
  de-identification and retention rules (open question 12). `export_jobs` keeps the request and progress only, never
  rows of data.

## D-61 Partner organisations and manual hand-offs (answer to open questions 13 and 33, in part)

- **Context:** Referrals were records only (D-45, D-53). On 2026-10-14 the product owner chose a manual hand-off: an
  electronic hand-off to SSA, Ma'an, FCA, SEHA or M42 needs signed agreements and credentials that do not exist.
- **Decision:**
  - `partner_organisations` lists partners: a unique snake-case code, the name in Arabic and English, the support
    categories it takes (1 to 20), and an active flag. System administrators register and deactivate partners
    (`/api/v1/admin/partners`); support coordinators read the list. None is seeded.
  - A support coordinator hands an open referral to an active partner that covers its category
    (`POST /api/v1/admin/referrals/{referralId}/hand-off`). The hand-off is recorded as `manual`, with the partner,
    the coordinator's subject and the time; the referral moves to InProgress and the partner's code becomes its
    assigned service. The member's referral consent and the partner are read `FOR SHARE`: the consent must be the
    referral's own and active, and a deactivation in flight is seen as `referrals.partner_inactive` (409).
  - The database guard (`AddPartnerHandOffGuards`) keeps the hand-off columns unchanged except in the move from Open to
    InProgress, and refuses an inactive partner or one that does not cover the category.
  - Support coordinators see the referral queue with the household reference the family can quote
    (`GET /api/v1/admin/referrals`, audited). Once the member withdraws referral consent the reference is withheld,
    but the cancelled referral and its hand-off stay listed (`?status=cancelled`), so the coordinator knows which
    partner to tell. Participants see the partner and `method: manual` on their referral.
  - The platform sends partners nothing and receives nothing from them. Every API response and document calls the
    hand-off manual.
- **Consequences:** What a coordinator may tell a partner outside the platform is governed by the consent text (open
  question 9) and the partner agreements, which do not exist yet. An electronic pathway or "automated case transfer"
  (open question 33) is not built. Resolving referrals after a hand-off is not built either.

## D-62 Score distributions for researchers only

- **Context:** The branch review found two disclosures in D-59. A suppressed distribution still listed the tiers that
  occur, so one listed tier told a group of 1 to 9 people their tier. And program administrators, who see which
  household was scored and when (`GET /api/v1/admin/assessments`), could read the overview before and after one
  submission and learn that household's tier. The spec contradicts itself here: section 2.3 says program
  administrators never see scores, section 4.6 gives them tier distributions.
- **Decision:**
  - Score distributions leave the overview. `GET /api/v1/admin/analytics/score-distributions` (optional `waveId`)
    serves them to researchers only, and each read is audited as `analytics.score_distributions.read`. Section 2.3
    wins: program administrators see progress, never scores. The permission `analytics.scores.read` tells clients
    who may call it.
  - A distribution whose total is suppressed lists no tiers.
- **Consequences:** The overview carries counts and completion only. Researchers could still compare reads over time,
  but they cannot see which household was scored when. The admin dashboard must call the separate endpoint.

## D-63 Mobile UI foundation

- **Decision:** The participant app's design system lives in `apps/mobile/src/design-system`. The brand is one file
  (`brand.ts`) and components use the semantic roles in `tokens.ts`. Components include `AppText`, `AppButton`,
  `AppInput`, `AppCard`, `AppHeader`, `Screen`, `FormField`, `SwitchField`, `RadioCardGroup` and `LikertScale`, with
  48-point touch targets. One stack navigator is guarded with `Stack.Protected` and has no tab bar. Directional icons
  mirror from the measured layout direction. Hard-coded text in screens is a lint error. The component showcase is the
  development-only route `/dev/design-system`. Design: `apps/mobile/docs/ui-foundation-design.md` (M-1 to M-7, M-18,
  M-19, M-21, M-22).
- **Consequences:** Replacing the brand edits one file. No Storybook.

## D-64 Mobile modes and sign-in

Superseded in part by D-83 (sign-in against the API).

- **Decision:** One switch, `EXPO_PUBLIC_DEV_FIXTURES=true`, honoured only in development builds, selects development
  fixtures mode: a simulated sign-in (no identity checked, no token) and the in-memory fixture API, labelled on every
  screen. Otherwise the app calls the real API, and an OpenID Connect placeholder reports sign-in as not configured
  until phase 1. A 401 ends the session as expired, and leaving the signed-in state drops cached data (M-8 to M-11).
- **Consequences:** A release build never shows fabricated data, and nothing implies a working sign-in (D-6).

## D-65 Mobile drafts before the encryption decision

Superseded in part by D-70 (where drafts live) and D-71 (saving and submission).

- **Decision:** Draft answers are kept on the device (SQLite `draft_answers`) only in development fixtures mode,
  where all data is synthetic. Against the API they stay in memory until open question 7 is decided, and neither
  store claims encryption. Drafts belong to one account (`drafts_owner`): signing in as someone else deletes them, and
  so does signing out. Answers sync after a short pause through one `AnswerSync` per session, which marks only the
  sent revision as synced. A refusal for good (400, 404, or 409 `sessions.not_in_progress`) discards them; other
  conflicts are retried. Submission sends an `Idempotency-Key` that is reused on retry (M-12 to M-14, M-23).
- **Consequences:** D-16 holds: no participant answers are kept on a device until encryption is decided.

## D-66 Mobile results and problem codes

Superseded in part by D-72 (what a complete result shows).

- **Decision:** The result screen shows the server's state. A complete result shows the score and tier code as
  recorded, under a notice that explanations await approval (open questions 2 and 3). Server problems are shown by
  their `code` as `problems.<code>` translations, falling back to the error kind; the server's `title` and `detail` are
  never shown. Dates and numbers use Western digits in both languages (M-15 to M-17).

## D-67 Mobile household enrollment

- **Decision:** The caller creates the household with only themselves; they become its manager, so they must be 18
  or over. Everyone else is then added one at a time, so the server is the record from the first step and nothing
  about the family is kept on the device. Only approved fields are asked: year and month of birth, relationship to the
  head, work status (required for adults), and head of household and main earner as two separate roles, open to adults
  only. Names and identity numbers are never asked. Household size and the presence of children come from the roster
  (D-24) and are not asked. The approved roster rules (D-50, D-58) only guide the forms; the server checks them again
  and its problem code is shown. Moving a role clears it from the old holder first and gives it back if the move
  fails. A confirmed household can confirm again, which is how it asks for a newly opened round's questionnaires.
  Design: `apps/mobile/docs/household-enrollment-design.md` (M-24 to M-26, M-32, M-34).
- **Consequences:** The same screens serve first enrollment and later changes.

## D-68 Mobile safe retries and duplicate submits

- **Decision:** After a failure that may have reached the server (no connection, a timeout, a 5xx), the next attempt
  reads first. Creating a household checks `GET /me`, and `409 households.already_member` leads to the existing
  household. When a member is added again, an active member who was not on the roster before the first attempt is
  taken as the one the lost request created. Updates are merge patches against the server's current record. Removing a
  member treats `404 households.member_not_found` after an uncertain attempt as done. Confirming the roster is
  repeatable (D-35). Starting a session that already exists (`409 sessions.session_already_exists`) opens it. Each
  action runs once at a time, a form locks after a successful save, and leaving with unsaved changes asks first
  (M-27 to M-29).
- **Consequences:** `POST /households` and `POST …/members` accept no `Idempotency-Key`. Reading first is safe while
  only the manager changes the roster; an `Idempotency-Key` (D-54) would make it exact once joining a household exists.

## D-69 Mobile roster confirmation and assignments

- **Decision:** Confirming calls `confirm-roster`. The app sends no eligibility of its own and has no control that
  changes an assignment. The server's answer is kept for the assignments screen and shown even when refreshing other
  data afterwards fails. Assignments of members who answer in their own account show only their status (D-49), and
  that screen reads no answers (M-30, M-31).

## D-70 Answers kept on the phone are encrypted (answer to open question 7, for drafts)

- **Context:** The participant app must survive a restart, work offline for sessions already opened, and queue a
  submission made offline. That needs answers on the phone, and SQLite encryption cannot be assumed. Design:
  `apps/mobile/docs/assessment-flow-design.md` (M-35 to M-38).
- **Decision:**
  - **Encryption:** every answer and every session copy written to the phone is encrypted field by field with
    AES-256-GCM (expo-crypto's native implementation), with associated data that binds each value to its table,
    session and question.
  - **The key:** a random 256-bit key made on the phone and kept only in the platform keystore (expo-secure-store:
    iOS Keychain `WHEN_UNLOCKED_THIS_DEVICE_ONLY`, Android Keystore).
  - **In clear:** the database file is not encrypted (no SQLCipher), so IDs, times, revisions, sync states and the
    queued submissions' idempotency keys stay readable.
  - **Fallback:** without a working keystore and cipher (checked with a self-test at start-up), nothing sensitive is
    written: drafts stay in memory and the app says so.
  - **Both modes:** this applies in development fixtures mode and against the API.
  - **Deleting:** signing out, or another account signing in, deletes every local row and then the key, and
    `PRAGMA secure_delete` is on.
  - **A lost key:** values that can no longer be read are deleted and the person is told how many.
  - **Offline copies:** a copy of a session is kept only after the server returned it to the signed-in person. It is
    used only when the server cannot be reached and is deleted on a 404.
  - **Local migration 3** adds `session_copies` and `pending_submissions` and rebuilds `draft_answers` with an
    encrypted answer and the server change it was based on.
- **Consequences:**
  - Supersedes D-16's empty drafts table and the storage part of D-65.
  - Both modules ship in Expo Go, so the choice needs no development build; Android backup stays off.
  - Open question 7 is closed for drafts. Whether a whole-database cipher (SQLCipher, development builds only) is
    wanted later stays with the product owner.

## D-71 Mobile saving, conflicts, offline submission and fixed versions

- **Decision (M-39 to M-42, M-47):**
  - **Saving:** each choice is written to the phone at once (one row per question), then sent after an 800 ms pause.
    One save runs per session at a time, and an answer the server already has is not sent again.
  - **Retries:** failures retry with exponential backoff and full jitter (2 s base, 60 s cap, 6 attempts, then by
    hand), restarting when the connection returns or the app comes to the foreground. Nothing is sent while
    offline (expo-network, also driving TanStack Query's `onlineManager`). A 401 or 403 stops automatic retries.
  - **Conflicts:** each draft records the server's `lastModifiedAt` it was based on. A draft whose question changed
    on the server since then, to another option, is a conflict: it is not sent, and the person keeps one answer or
    the other. Submission waits for conflicts. Times are compared as instants: a save's response has 100 ns
    precision, a read has PostgreSQL's microseconds. The server is read again before sending whenever its answers
    may be stale (after the phone was offline, a network failure, or a restart).
  - **Submission:** a request recorded on the phone with its `Idempotency-Key` before anything is sent, processed
    when signed in, online and with every answer saved. Every retry, including after a restart, uses the same key.
    The app says "submitted" only after the server's 200, and answers are frozen while a submission waits (it can
    be cancelled).
  - **Versions:** a session is shown only with the version it was started on (`versionId` checked). A version that
    is unavailable or different shows a state of its own, never a substitute.
- **Consequences:**
  - Supersedes the sync part of D-65.
  - The save endpoint takes no precondition, so an answer changed elsewhere between the app's read and its save is
    overwritten. A precondition on `PUT /sessions/{id}/responses` (the `lastModifiedAt` the client saw) would
    close that window; it is proposed for the backend.

## D-72 Mobile question types, introduction and participant-facing results

- **Decision (M-43 to M-46):**
  - **Question types:** the questionnaire renderer supports each item type explicitly. The contract permits only
    `single_choice` (and the Sessions module refuses free text), so only that is built. An unknown type parses as
    `unsupported`, and such a questionnaire cannot be started or answered in that app version. Answers are checked
    with Zod schemas built from the published version.
  - **Introduction:** the version's own description, the number of questions, and a factual privacy notice. No
    duration, since the API gives none.
  - **Results:** participants are not shown a score, a tier or support recommendations until that display is
    approved (`RESULT_DISCLOSURE`, all off). The result screen confirms the submission and shows the server's result
    state; when a flag is turned on, the API's value is shown exactly as returned. The app never computes or infers
    a score or a tier.
- **Consequences:**
  - Supersedes D-66's display of a complete result's score and tier code.
  - Turning a flag on needs a recorded approval (open questions 2, 3 and 13). The privacy notice's wording joins the
    consent text in legal review (open question 9).

## D-73 Admin sign-in through a backend-for-frontend

- **Context:** The admin dashboard needs sign-in and API access without tokens in the browser (D-17), before a
  development OIDC issuer exists (phase 1). Spec `docs/superpowers/specs/2026-10-16-admin-dashboard-design.md`.
- **Decision:**
  - OIDC authorization code with PKCE (S256), `state` and `nonce`, run on the dashboard's server with `openid-client`
    6. The access and refresh tokens live in a JWE session cookie (`dir`, `A256GCM`, `jose`): HttpOnly,
    `SameSite=Lax`, `Secure` and `__Host-` over HTTPS, an absolute lifetime of 8 hours by default, split into chunks
    when long. Roles are never stored in it: `GET /api/v1/me/permissions` is read on every request.
  - The browser calls only `/bff/...`, an allowlist of API routes and query keys forwarded with the bearer token.
    State-changing requests must carry this app's `Origin`; bodies are JSON of at most 16 KB; responses keep only
    their content type (and a download's disposition) and are never cached.
  - `src/proxy.ts` sends requests without a session to sign-in and refreshes an access token a minute before it
    expires; a refresh token is spent once per server process, and requests that still carry the old cookie share
    the result for 30 seconds.
  - Development sign-in (a persona picker, with per-persona tokens from `dotnet user-jwts` for the real API) and a
    mock data source exist only when `ADMIN_ENVIRONMENT=development`; any other value is production and fails
    closed. Both are labelled on every page. Without `ADMIN_SESSION_SECRET`, development sign-in uses a key derived
    from the origin, because the proxy runs in its own bundle and a random per-process key did not match; OIDC always
    needs a real secret.
  - All settings are server-only; `NEXT_PUBLIC_API_URL` is gone and the health check goes through the BFF, so the API
    needs no CORS entry for the dashboard.
- **Consequences:** No token reaches browser storage or scripts. Several dashboard instances need a shared session
  store or sticky sessions before production, because refresh de-duplication is per process. UAE PASS is still not
  configured, and the sign-in page names no provider.

## D-74 Admin reads for the dashboard

- **Context:** The dashboard must not copy the export allowlist, invent wave IDs or compute counts the API does not
  return.
- **Decision:** Additive, read-only endpoints, each with integration tests and in the OpenAPI snapshot:
  - `GET /api/v1/admin/waves` (researchers and program administrators): waves, newest opening first.
  - `GET /api/v1/admin/exports` and `GET /api/v1/admin/exports/catalog` (researchers): the caller's own exports, newest
    first, and the datasets, columns and filters of `ExportCatalog`.
  - `GET /api/v1/admin/assessments` gains `versionNumber` (needs `instrumentCode`), `assignedFrom` and `assignedTo`
    (inclusive UTC days).
  - The overview's `assignments.started` counts assignments with at least one session, of any status.
  - `GET /api/v1/instruments/{instrumentId}/versions` and `GET /api/v1/instrument-versions/{versionId}/rule-sets`
    (system administrators): versions without content, and every rule set of a version.
  None of these reads holds personal data, so none is audited, like the existing single reads.
- **Consequences:** No migration. `AssignmentTotalsResponse` gained a field in the middle of its positional record;
  JSON clients are unaffected.

## D-75 Dashboard display rules

- **Decision:**
  - The dashboard formats the API's numbers; it computes no statistic, score, eligibility, rate or suppression. As a
    safeguard, a count from 1 to `minimumGroupSize − 1` that arrives unsuppressed is hidden anyway, and a demographic
    row with any hidden count shows no rate.
  - Each instrument version, module and rule set is shown on its own; across waves only the same key is compared, and
    only for keys scored in at least two of the three most recent waves (open question 35). Hidden tiers are listed,
    never drawn.
  - Pages filter by wave; "study cohorts" are not offered because the platform has none (open question 34).
    Demographic comparisons of scores show an empty state until one is approved (open question 36).
  - Questionnaires and rules are read-only. The API's approval, retirement and partner operations are not offered in
    the dashboard.
  - Charts use one validated hue for a single series and at most three validated hues, with a legend and a table
    view, for compared waves. Every chart has a caption, a summary and a table; dates are in UTC with Western digits.
- **Consequences:** Approving content and rules stays a deliberate act through the approval endpoints with a named
  approver. A support coordinator who signs in sees a welcome page until the referral queue is added.

## D-76 Strict token validation and a guard for deployed settings

- **Context:** The security review (spec `docs/superpowers/specs/2026-10-16-security-hardening-design.md`) found that
  token validation used framework defaults and that nothing stopped a deployment with an `http` authority, no
  audience or development signing keys.
- **Decision:**
  - After the framework binds `Authentication:Schemes:Bearer` (which replaces the validation parameters), a
    post-configure step forces issuer, audience, lifetime and signature validation, requires `exp` and a signature,
    accepts only RS/PS/ES 256–512 (plus HS256 in Development for `dotnet user-jwts`), sets one minute of clock skew
    (`Authentication:ClockSkewSeconds`, at most 300) and turns error details off, so the challenge is a bare `Bearer`.
  - A rejected token is logged by a fixed reason (`expired`, `signature`, `issuer`, `audience`, `lifetime`,
    `algorithm`, `malformed`, `other`) and counted in `azwah.auth.tokens_rejected`; the token and the exception text
    are never logged.
  - "Deployed" means any environment except Development and Testing. A deployed host refuses to start, naming problem
    codes only, without an `https` authority and an audience, with `SigningKeys`, plain-HTTP metadata, a wildcard or
    plain-HTTP CORS origin, a database `SSL Mode` other than Require, VerifyCA or VerifyFull, forwarded headers
    trusted from anyone (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`, or a `ReverseProxy:KnownNetworks` range wider than /8
    for IPv4 or /32 for IPv6), or `RateLimiting:Enabled=false`.
  - In deployed hosts a token carrying `azwah_development_only` (every token of the development identity provider,
    D-82) is refused after validation, even when its signature is trusted.
- **Consequences:** A misconfigured deployment fails at start-up instead of running weakened. A deployment must never
  run as Development or Testing. IdentityModel reports a disallowed algorithm as a signature failure.

## D-77 TLS in deployed environments

- **Decision:** Deployed hosts send HSTS (365 days, no `includeSubDomains` or `preload` until the domain is known) and
  answer any non-HTTPS request with 400 `requests.https_required`, without running it and without redirecting; health
  probes are exempt. Forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`, one hop) are honoured only from
  `ReverseProxy:KnownProxies` (IPs) or `ReverseProxy:KnownNetworks` (CIDR). Kestrel sends no `Server` header.
- **Consequences:** A client that sends a token over HTTP fails instead of being redirected. The TLS-terminating proxy
  must be listed.

## D-78 Security headers and request limits

- **Decision:** Every API response carries `Cache-Control: no-store` (unless the endpoint set its own), `nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` and `Content-Security-Policy: default-src 'none';
  frame-ancestors 'none'; base-uri 'none'; form-action 'none'`. Kestrel limits bodies to 1 MiB (ten times the largest
  content import), headers to 32 KiB and request lines to 8 KiB; existing per-endpoint limits stay.
- **Consequences:** No intermediary or device caches participant data. The test server does not enforce Kestrel
  limits; a test checks the configured values.

## D-79 Rate limiting

- **Decision:** ASP.NET Core's rate limiter runs between authentication and authorization. Every request passes a
  global fixed window per token subject (300 a minute) or, when anonymous, per remote address (60 a minute; IPv6 by
  /64); health is exempt. Named policies apply to signed-in callers only (an anonymous caller is refused by
  authorization anyway, and a long-window partition per address would only cost memory), per subject: `participant-writes` 30/min, `answers` 120/min, `identity` 10/min,
  `staff-reads` 60/min, `staff-writes` 30/min, `exports` 10/hour, `export-downloads` 30/hour. A refusal is 429
  `requests.rate_limited` with `Retry-After`, logged with the policy name and counted. Settings under `RateLimiting`
  (numbers clamped to at least 1; only `Enabled=false` turns it off, which the shared test hosts do and a deployed host
  refuses, D-76). The log and metric name the endpoint's policy, which may not be the limit that refused. The OpenAPI
  contract declares 429 on every operation. A test fails when a state-changing endpoint has no named policy.
- **Consequences:** Limits are per API instance; several instances need a shared store or edge rate limiting. The
  mobile app retries 429 with its backoff; the dashboard shows a translated state.

## D-80 Export quotas

- **Decision:** After the idempotent replay check, `CreateExport` refuses a researcher who already has
  `Exports:MaxActivePerRequester` (2, at most 10) queued or running exports (409 `analytics.export_limit_reached`) or
  `Exports:MaxPerRequesterPerDay` (20, at most 200) requests in the last 24 hours (409
  `analytics.export_daily_limit_reached`).
- **Consequences:** A retry of an accepted request is still replayed. Two simultaneous requests can both pass the count;
  the `exports` rate limit bounds that.

## D-81 Authentication, authorization and role events in the audit log; append-only enforced

- **Decision:**
  - `POST /api/v1/me/sign-ins` and `/sign-outs` (body `{"client":"mobile"|"admin"}`, 204, any signed-in subject)
    record `identity.session.started` and `identity.session.ended` against resource type `auth_client`. The apps call
    them best effort; the identity provider's log stays authoritative.
  - `AuthorizationResultHandler` audits every refusal of a signed-in caller: `authorization.resource.denied` (resource
    type and route ID, answered 404) or `authorization.role.denied` (route template, answered 403), outcome Denied.
    Anonymous refusals are not stored, so they cannot grow the table.
  - Roles are granted in the identity provider, so the platform audits what it observes: `role_snapshots` keeps the
    known platform roles last seen for each subject; after authentication a middleware (with a ten-minute in-memory
    cache, in its own DI scope) records `identity.role.added` or `identity.role.removed` (resource type
    `platform_role`) for each difference, the first request counting every role as added. A unique-subject or
    concurrency conflict means another request recorded it. Failures are logged and the request goes on.
  - Migration `AddAuditEventGuards`: DELETE on `audit_events` is refused unless the transaction sets
    `azwah.audit_retention = 'on'` (reserved for an approved retention job); TRUNCATE is always refused.
- **Consequences:** The first request of every subject writes its role events, and tests that count a subject's audit
  events must filter by action. Production must also restrict the application role's grants on `audit_events`.

## D-82 Development identity provider

- **Context:** Phase 1 needed an OpenID Connect issuer to sign in against in development; a mock that mints tokens
  must never be able to run inside the deployed API.
- **Decision:** `backend/FamilyResilience.DevIdentity` is a separate ASP.NET Core app with no project references and
  its own Dockerfile. It refuses to start outside Development, labels every page as simulated, and adds
  `"azwah_development_only": true` to discovery. It implements discovery, JWKS (an ephemeral RSA-2048 key), the
  authorization code flow with S256 PKCE, `state`, `nonce` and RFC 9207 `iss`, single-use 60-second codes, refresh
  rotation with family revocation on reuse, RFC 7009 revocation and end-session to registered URIs only. Clients
  `azwah-mobile` (public; `azwah://auth/callback` and Expo Go `exp://…/--/auth/callback`) and `azwah-admin`
  (confidential; its secret has no default). Seven fixed personas with stable subjects and roles; no personal claims.
  Compose runs it as `dev-identity` and the Compose API trusts it; `dotnet run` users opt in through user secrets.
  Every token it issues carries `"azwah_development_only": true`, which a deployed API refuses (D-76). It listens on
  127.0.0.1 by default (Compose `DEV_IDENTITY_BIND`, `dotnet run --urls` to opt in to the network), because anyone who
  reaches it can mint any persona's token for the local API.
- **Consequences:** The full token path is tested end to end (`End_to_end_the_api_accepts_a_development_token`). Every
  client must reach the issuer under the same URL (`adb reverse` or a LAN issuer for phones). It says nothing about a
  production provider.

## D-83 Mobile OpenID Connect sign-in

- **Context:** D-64 left the app's sign-in as an honest placeholder until phase 1.
- **Decision:**
  - `EXPO_PUBLIC_OIDC_ISSUER` and `EXPO_PUBLIC_OIDC_CLIENT_ID` turn sign-in on (there is no secret variable);
    `EXPO_PUBLIC_OIDC_SCOPE` (default `openid offline_access`), `EXPO_PUBLIC_OIDC_RESOURCE` and
    `EXPO_PUBLIC_OIDC_PROMPT` (default `login`, so a shared phone asks again) are optional. Without issuer or client the
    placeholder stays.
  - Discovery is Zod-validated; the issuer must match exactly; `http` endpoints and development-only issuers are
    refused outside development builds.
  - Sign-in runs through `expo-web-browser` `openAuthSessionAsync` (ephemeral on iOS). The callback must match the
    redirect URI, `state` and `iss`; ID token claims are checked, not its signature (OIDC Core 3.1.3.7).
  - The refresh token and subject are stored with expo-secure-store (`WHEN_UNLOCKED_THIS_DEVICE_ONLY`); access and ID
    tokens stay in memory. Renewal 60 s before expiry, one at a time; `invalid_grant` or another 4xx ends the session as
    expired; a network failure keeps it and surfaces `ApiError('network')`. A 401 still ends the session (D-64).
  - Sign-out records the end, revokes the refresh token and clears the keystore; local data is deleted as before
    (D-70). `app/+native-intent.tsx` keeps the router off the callback links.
  - 429 is `ApiError('rate_limited')`, retryable; answer saving and the submission queue retry it.
  - A release build accepts only an `https` `EXPO_PUBLIC_API_URL`; anything else reads as not configured.
- **Consequences:** Supersedes the sign-in part of D-64. Not yet tried on a device against the development provider.

## D-84 Admin sign-in records and revocation at sign-out

- **Decision:** After the OIDC callback and after development sign-in against the API, the dashboard's server records
  the sign-in (best effort, 3-second timeout). Sign-out renews an access token that has expired (or expires within 30
  seconds), so the record is not refused, records the end, revokes the newest refresh token when the issuer has a
  revocation endpoint, then clears the cookie and ends the issuer session as before (D-73). 429 is its own translated
  state; the two export quota codes have messages.
- **Consequences:** A copy of the session cookie taken before sign-out can no longer be refreshed.

## D-85 Data classification, minimisation guard and retention placeholder

- **Decision:** Data is classified as Public, Internal, Confidential or Sensitive participant data, with an inventory
  of every store (`security-model.md`). `DataMinimisationTests` fails when a column name suggests a direct identifier
  unless it is on an approved allowlist (empty). `docs/retention-policy.md` is a draft that lists every category,
  the periods in code, the erasure order and the approvals needed, and must be approved before production. Hosting,
  encryption-at-rest, database-role and secret requirements are recorded; no hosting is selected (open question 6).
  `docs/threat-model.md` records threats, mitigations, residual risks and the before-production list.
- **Consequences:** Adding a personal field is a recorded decision first. Nothing is deleted on an unapproved schedule.

## D-86 Separate database roles for migrations and the application

- **Context:** The API connected as the schema owner, so a flaw in the application could change the schema or rewrite
  the audit trail (threat model).
- **Decision:** `Persistence/Sql/database-roles.sql` (run once by a database administrator) creates `azwah_migrator`
  (owns the schema) and `azwah_app`, both LOGIN with passwords set from the secret store, never in a file, and removes
  `CREATE` on schema `public` from `PUBLIC`. `application-role.sql`, run by the owner after every migration and safe
  to run again, gives `azwah_app` `SELECT, INSERT, UPDATE, DELETE` on every table, only `SELECT, INSERT` on
  `audit_events`, only `SELECT` on `__EFMigrationsHistory`, sequence use, and the same through default privileges for
  later tables. It never gets DDL, `TRUNCATE`, `REFERENCES` or `TRIGGER`. `ConnectionStrings:Migrations`, when set, is
  the connection `db migrate` and start-up migrations use; deployed hosts refuse `Database:ApplyMigrationsOnStartup=true`
  (`migrations_on_startup`).
- **Consequences:** `DatabaseRoleTests` proves the refusals (SQLSTATE 42501) and `ApplicationRoleJourneyTests` runs
  enrolment to scoring as `azwah_app`. Local development may keep one owner connection.

## D-87 Dependency scanning

- **Decision:** The backend workflow fails when `dotnet list package --vulnerable --include-transitive` lists a
  package. The admin and mobile workflows pipe `npm audit --json` into `scripts/npm-audit-gate.mjs`, which fails on a
  high or critical advisory unless the app's `audit-allowlist.json` lists it with a reason and a `reviewBy` date that
  has not passed. Two advisories have no fixed release and reach only build or test tooling, so they are allowlisted
  until 2027-01-17: braces GHSA-vfj7-8cjw-p6xm (both apps) and node-forge GHSA-86w9-cpqp-85rv (the Expo CLI, mobile).
  Moderate advisories are reported, not gated. The gate fails when `npm audit` produced no report (a registry or
  network failure is not "nothing found"), and has its own `node --test` tests; the .NET listing runs with `pipefail`.
  Dependabot proposes NuGet, npm and GitHub Actions updates weekly; for
  mobile it leaves Expo SDK and React Native packages alone, since they move together through `npx expo install`. The
  demo is left out.
- **Consequences:** An allowlist entry past its date fails the build until someone reviews it again.

## D-88 Household erasure and the retention job (mechanisms; periods not approved)

- **Decision:**
  - `EraseHousehold` erases a Withdrawn household in one transaction with `SET LOCAL azwah.erasure = 'on'`:
    recommendations, referrals, score results, responses, sessions, assignments, privacy requests, consent records,
    memberships, members, the household, platform users left without a membership and their role snapshots, then
    idempotency keys and outbox messages that name an erased ID.
  - Audit events stay; one `households.household.erased` event is written in the same transaction, after one
    `privacy.request.closed` event for each of the household's open privacy requests (the erasure honours them and
    deletes them, so there is nothing left for staff to close).
  - The household's row is locked first (`SELECT … FOR UPDATE`): a second erasure of the same household waits, then
    answers `households.household_not_found`, so nothing is erased or audited twice.
  - It refuses until `Erasure:ConsentRecords` is exactly `delete` (open question 21); any other value means not built.
    It also refuses a household that is still active (`--withdraw` withdraws it first, audited) and a reference that
    does not match.
  - The operator command is `db erase-household --household <id> --reference <ref> [--withdraw]`. It prints counts
    per table, never data.
  - `ApplyRetention` runs in the maintenance service and does nothing unless `Retention:Enabled=true`. Each period has
    no default, and a period that is not set deletes nothing:
    - `Retention:WithdrawnHouseholdDays` erases households withdrawn longer ago than that, by `updated_at`, at most 50
      a run, each in its own scope. A household that is refused or faults is rolled back and counted as a failure (a
      fault as `exception:<type>`, never its message), and the run goes on; one another run has just erased is
      skipped. The outbox and export-job deletes run first.
    - `Retention:ProcessedOutboxDays` deletes processed outbox messages.
    - `Retention:FinishedExportJobsDays` deletes failed and expired export jobs.
  - Audit events are not covered.
- **Consequences:** Erasure can honour an approved request today, and it works under the application role's grants
  (`ApplicationRoleJourneyTests`). `HouseholdErasureTests` reads the foreign keys from `pg_constraint` and fails when a
  table that points at erased rows is neither erased nor cascaded. Nothing is deleted on a schedule until the periods
  in `retention-policy.md` are approved and configured (open question 22).

## D-89 Rights requests: a copy of the data, and requests staff handle

- **Decision:**
  - `GET /api/v1/me/data-export` returns the caller's household and the members they may act for (D-49), with their
    consents, assignments, sessions and every answer, referrals and privacy requests. It is audited
    (`privacy.data_export.read`) and limited to 10 an hour.
  - Score results and support recommendations are left out and named in `notIncluded`, because participant-facing
    results are not approved (D-72) **(engineering call; open question 39 decides)**.
  - `POST /api/v1/me/privacy-requests` records an access, correction or erasure request for a member the caller may
    act for. It takes no free text: details go through the programme's support channel. One open request per member
    and type is allowed (409 `privacy.request_already_open`), and each is audited.
  - Program administrators list the requests (`privacy.requests.manage`; open ones first, with the household
    reference) and close them as completed, declined or withdrawn, audited **(engineering call: they already find
    households by reference)**.
  - An erasure itself is the operator command (D-88). The mobile profile screen offers the download (a JSON file
    through the share sheet, deleted afterwards) and the request form; the dashboard has `/privacy-requests`.
- **Consequences:** Who answers, how fast and on which legal basis remain open (open question 39). The export file
  leaves the platform at the family's choice.

## D-90 Security counters and alert rules

- **Decision:** The `FamilyResilience.Api.Security` meter (one per host, from `IMeterFactory`) counts:
  - rejected tokens (`reason`);
  - rate-limited requests (`endpoint_policy`);
  - signed-in authorization refusals (`azwah.authorization.denied{kind=resource|role}`, as audited, D-81);
  - observed role changes (`azwah.identity.role_changes{change, role, first_seen}`);
  - retention erasures and erasure failures.

  Tags are fixed codes only, never subjects or IDs; the audit trail says who. `infra/monitoring/alert-rules.yml`
  alerts on:
  - refusal bursts (more than 50 in 10 minutes per kind);
  - any staff role appearing (critical; silence it for an announced change window);
  - staff role removals (info);
  - a development token at a deployed API (critical);
  - token-rejection surges;
  - rate limiting sustained for 15 minutes;
  - retention erasures and failures.

  A counter series exists only from its first increment, so each rule also fires on a series that is new within its
  window; otherwise the first staff grant or development token after a restart would go unseen. The thresholds are
  starting values **(engineering call)**. `alert-rules.test.yml` proves each rule with promtool, starting series
  absent as the SDK does, and the backend workflow runs it.
- **Consequences:** Nothing pages anyone until an operator connects Alertmanager to a receiver. A participant role on
  a subject's first request is expected and does not alert.

## D-91 Mobile inactivity lock and app-switcher cover

- **Decision:**
  - While the app is inactive or in the background, a brand cover replaces the screen, so the app switcher shows no
    answers.
  - Signed in, the app locks after more than 2 minutes in the background, after 10 minutes without a touch, and on a
    cold start that restores a session. No route renders behind the lock screen.
  - Unlocking uses `expo-local-authentication` (biometrics, or the device passcode).
  - With no enrolled authentication, or on "Sign in again", the app signs out, which deletes local data (D-70); the
    lock screen says that unsent drafts are lost.
  - The times are constants in `src/security/lockTimes.ts` **(engineering call)**. Development fixtures mode locks the
    same way.
  - While locked, the signed-in screens are unmounted, not hidden. After unlocking, the app returns once to the screen
    the person was on (the path is kept in memory only); later visits to the start route lead home.
  - Activity for the idle lock is a touch, a new screen or an answer chosen; typing in a text field alone does not
    count. Time in the system's unlock prompt does not count as time away.
  - Time is measured on the wall clock and on `performance.now()`, and the longer reading counts; the steady clock may
    not count time the phone sleeps, so it only ever adds. A wall clock set back while the app was away locks it, and a
    backwards wall clock cannot delay the idle lock.
  - A passcode-only phone can unlock. The outcome is announced to VoiceOver and TalkBack.
  - The reload after a language change does not lock: the switcher records the time (`language_changed_at`), and
    start-up honours it once, within 30 seconds.
  - **Data download (D-89):** the export request never waits for the network (`networkMode: 'always'`), so an offline
    tap fails at once and is never sent later on its own. The file reaches the share sheet only while the app is
    unlocked and in the foreground (checked just before `shareAsync`). It is written from the body the server sent,
    after Zod validation, so the copy keeps fields the app does not know. It lives only in the cache folder
    `azwah-exports`, which is emptied after sharing, before each export, at start-up, at sign-out, on session expiry
    and whenever the lock engages.
  - **Android Recents:** on Android 13 and later a local config plugin (`plugins/withRecentsScreenshotDisabled.js`)
    calls `setRecentsScreenshotEnabled(false)` in `MainActivity.onCreate`, because the JavaScript cover is drawn too
    late for the Recents snapshot. Android 12 and older rely on the cover. Screenshots are not blocked (`FLAG_SECURE`
    stays a non-goal).
  - The iOS Face ID text stays English only: an Arabic one needs Expo's `locales`, which adds `ar.lproj` to the iOS
    project, and React Native would then lay the app out right to left on Arabic iPhones whatever language is chosen
    in the app (`RCTI18nUtil`; the problem of D-15).
- **Consequences:** A phone left unlocked and unattended no longer shows a family's answers. Not yet tried on a device:
  the biometric and passcode prompts, the app-switcher snapshot, the Recents plugin (its `expo prebuild` output was
  checked once, never compiled) and the share sheet. What remains open by design or platform:
  - A share sheet that is already open stays above the lock screen and the cover; it cannot be closed from JavaScript,
    but its file is deleted when the lock engages, so share targets can no longer read it.
  - On Android `shareAsync` returns when the chooser closes, so deleting the file then can cut off a receiver that reads
    it later.
  - A process killed while the sheet is open leaves the file until the next start.
  - A native confirmation dialog open when the app leaves the foreground can stay above the cover; those dialogs hold
    no answers.

## D-92 Support-resource directory

- **Context:** The brief asks that families and coordinators can find public support. No approved directory exists in
  the repository, the sources or this log; the partner registry (D-61) is a referral tool with no descriptions, links
  or audiences. Design: `superpowers/specs/2026-10-18-support-resource-discovery-design.md`; as built:
  [`support-resource-discovery.md`](support-resource-discovery.md).
- **Decision:**
  - A new module, `SupportResources`, separate from Referrals: the directory is public information a person browses,
    while referrals are consented hand-offs routed by approved rules. The module never references Sessions, Scoring,
    Referrals, Assignments, Households, Members, Consent, Questionnaires, Analytics or Privacy
    (`SupportResourcesIsolationTests`). A search or read uses nothing about the caller beyond the token's roles;
    approvals record the administrator's subject, and every admin change records it in the audit trail.
  - `SupportResource` is versioned per code: Draft → Approved → Retired, at most one approved version per code. System
    administrators draft, edit drafts, approve and retire through `/api/v1/admin/support-resources` (policy
    `SystemAdministrator`, permission `support_resources.manage`, rate limit `staff-writes` on writes). Each change is
    audited: `support_resources.resource.drafted`, `…updated`, `…approved`, `…retired` (resource type
    `support_resource`).
  - Approval needs Arabic for every text, records the approver, time and source, and sets a review date later than the
    approval and at most 366 days after it **(engineering call; open question 41 asks for the cadence)**. Approving a
    version first retires the code's approved version in the same transaction (two commits), so any draft, older or
    newer, can be approved and a rollback is possible.
  - An entry is found only while Approved and before its review date. An overdue entry stays Approved but leaves search
    and the reader endpoint (fail closed on stale information), and the admin response flags `reviewOverdue`. Approving
    the next version re-verifies it.
  - Visibility is `Everyone` or `StaffOnly`; support coordinators and system administrators see both, everyone else
    `Everyone`, decided on the server from the token's roles.
  - An entry holds names, a summary and a provider in Arabic and English, an `https` link, 1 to 10 categories, up to 10
    audiences and 1 to 5 service languages. No fees, phone numbers, emails, addresses or eligibility criteria: the
    linked source holds them.
  - Approved and retired rows are immutable in the database (`trg_support_resources_immutable`, SQLSTATE 23001).
  - Nothing is seeded; no approved content exists. Tests use synthetic entries only.
- **Consequences:** The directory stays empty until someone owns its content (open question 41). Categories and
  audiences are free snake-case codes, like partner categories. The table references no household or member, so
  erasure is unaffected.

## D-93 Deterministic support-resource search

- **Decision:**
  - `POST /api/v1/support-resources/search` and `GET /api/v1/support-resources/{resourceId}`, policy
    `SupportResourceReader` (`participant`, `support_coordinator` or `system_administrator`), permission
    `support_resources.read`. Search is a POST so free text never sits in a URL, proxy log or browser history; it has
    the rate limit `support-searches` (30 a minute). The request is `{text?, categories?, audiences?, limit?}`: text up
    to 200 characters, up to 5 categories and 5 audiences, limit 1 to 10 (default 5), and text or a category. The
    reader endpoint answers 404 unless the entry is findable now and visible to the caller.
  - PostgreSQL full-text search over a stored generated `tsvector` (names weight A, summaries B, providers C): English
    with configuration `english`; Arabic with `simple` after normalisation, because the `arabic` snowball stemmer
    conflates badly (`الديون` → `الد`, `ديوني` → `ديو`) and keeps function words. Query text keeps letters only.
    Arabic terms are folded, stripped of diacritics, tatweel and one leading article, filtered against a function-word
    list, stripped of one suffix and matched as prefixes. English terms are lowercased and PostgreSQL's English stop
    words are dropped. At most 8 terms. The document side applies the same folding and article stripping in SQL from
    one C# constant, with a parity test.
  - One parameterised statement filters first (status, review date, visibility, categories, audiences); a resource then
    needs at least half of the usable terms, and at least one **(engineering call)**. Order: matched terms, summed
    `ts_rank_cd`, code, version, ID. No usable terms with a category is a browse by code. No text and no category is a
    400; text that leaves no terms is a 200 `no_match`.
  - Every answer carries `notice: information_only`; each match is a directory row rendered from approved fields with
    its ID and URL.
  - Searches and reads are not audited: the results are public, and an audit row would link a subject to a sensitive
    need for no benefit **(engineering call)**. Search text is never stored, logged, audited, put in a metric tag or put
    in a URL; the module writes no logs.
  - Meter `FamilyResilience.SupportResources`: `azwah.support_resources.searches` (`mode`, `outcome`, `fallback`) and
    `azwah.support_resources.search_duration` (`mode`, milliseconds), fixed codes only.
- **Consequences:** Search works without any AI. Arabic recall is weaker than English on the synthetic set (D-95). The
  half-coverage rule, the 8-term budget and the function-word list were tuned on synthetic queries only.

## D-94 The language-model path is built and dormant

- **Context:** The brief asks for an optional AI assistant behind an interface, with deterministic search as the
  fallback. No AI use case is approved; no provider, region or data-processing agreement exists (open questions 6 and
  42); nothing approves sending a person's text to a model.
- **Decision:**
  - `ISupportResourceAssistant` has two implementations: `DeterministicSupportResourceAssistant`, the default and the
    fallback, and `LanguageModelSupportResourceAssistant`, chosen only when `SupportResources:LanguageModel:Enabled` is
    true. Its provider port, `ILanguageModelClient`, has no implementation: **no adapter ships**, and no HTTP client to
    any AI service exists.
  - Switched on, the API refuses to start in any environment (`LanguageModelStartupGuard`) unless `ApprovalReference`
    names the approving decision (at most 100 characters), an `ILanguageModelClient` is registered, and the timeout is
    1 to 30 s, attempts 1 to 3, candidates 1 to 20 and output tokens 64 to 1024. The error names problem codes only.
  - **Selection only.** The model receives the fixed instructions (version `support_resource_selection.v1`) and one JSON
    data document: the redacted text (no emails, links, Emirates ID–shaped strings or digits) and up to
    `MaxCandidates` candidates from the same filtered search, with coverage relaxed to one term, under handles
    `r1…rN`, never IDs, codes or URLs. It may answer only `{"selected": [handles]}`. `SupportResourceSelectionGuard`
    rejects anything else and never throws. Matches are re-rendered from directory rows. The model writes no text a
    family sees; summaries are not allowed.
  - Each attempt has its own timeout; only timeouts and transient faults are retried, up to `MaxAttempts` attempts.
    Every failure answers with the strict deterministic search, `mode: deterministic` and a fallback code
    (`model_timeout`, `model_unavailable`, `model_output_invalid`, `model_output_ungrounded`, and
    `model_selected_none` for an empty selection, so a model can never hide all help). The caller's cancellation
    propagates. Without terms or without candidates the model is not asked.
  - `azwah.support_resources.model_calls` (`result`) and `azwah.support_resources.model_tokens` (`direction`) count
    attempts and tokens. A future adapter must be truly asynchronous (a client that blocks before returning its task
    cannot be cut off by the timeout) and may log at most a fixed category and an exception type name, never a prompt,
    reply or query.
  - **Prerequisites before it is switched on:** an approved use case with a named owner; approved directory content in
    production (open question 41); a provider with a data-processing agreement, residency and retention terms and no
    training on our data (6, 42); a privacy impact assessment covering free text sent outside the platform (40); a
    reviewed adapter in Infrastructure with credentials in the secret store; the evaluation run on the real directory
    with the real adapter, meeting agreed floors for recall, grounding and schema failures and budgets for latency and
    cost; reviewed participant wording for the notice and an "ordered with AI assistance" label (43); alert rules for
    fallback and token surges; and a decision record whose reference goes in `ApprovalReference`. Model summaries need
    all of that, a text guard and a separate approval.
- **Consequences:** No AI assistant is available, and nothing may say one is. Test hosts switch the path on with
  scripted models and the reference `TEST-ONLY`; no other host can, because no adapter exists.

## D-95 Support-resource evaluation and the semantic-retrieval rule

- **Decision:**
  - A synthetic evaluation in the integration tests (`SupportResources/Evaluation`): 24 bilingual resources and 48
    queries (40 in scope, half Arabic; 8 out of scope), in a collection with its own database.
    `RetrievalEvaluationTests` holds deterministic retrieval to regression floors set at or just under the values
    measured on 2026-10-18, rounded down to a multiple of 0.05; four equal their measurement. Ranking is deterministic,
    so the floors do not flake, and a drop means the search or the set changed. There is no latency floor.
  - `AssistantGuardEvaluationTests` runs six scripted model behaviours through the real API, prompt and guard. Final
    invalid matches are 0 for every behaviour; fabricating, schema-breaking and slow models fall back every time. This
    measures the guard, not any real model.
  - Measured after tuning, R@5 is 0.850 in English and 0.575 in Arabic (0.750 and 0.425 before). The set is synthetic
    and several queries reuse summary wording, so these numbers are an optimistic upper bound for keyword search and
    the Arabic gap is indicative only; they are not evidence about a real directory.
  - **Semantic-retrieval rule:** embeddings or a vector database are added only if, on the real approved directory and
    an approved query set, recall@5 stays under an agreed floor after keyword search has been tuned, with the gap shown
    per language. Even then: embeddings of approved public descriptions only, never answers, and no stored queries;
    retrieval still filtered by status, review date and visibility first; results still carrying IDs and links; and a
    recorded decision. No vector database is added now (`CONTRIBUTING.md`, rule 14).
- **Consequences:** The Arabic misses left are known: broken plurals, verbs against verbal nouns, attached one-letter
  prepositions and conjunctions, dialect vocabulary, and the article rule that turns `والدين` into `دين`. Fixing the
  last needs a migration. Arabic stemming or semantic retrieval waits for a real directory and query set.

## D-96 UAE PASS staging sign-in through the development identity provider

- **Context:** The app needs to show a real UAE PASS sign-in and the details UAE PASS releases before any production
  onboarding exists (open questions 5, 37). UAE PASS publishes a sandbox client for its staging environment
  (`sandbox_stage`, docs.uaepass.ae) that accepts any redirect URI. Staging identities are test accounts made in the UAE
  PASS staging app; production identities are real people.
- **Decision:**
  - The development identity provider (D-82) gains an optional upstream sign-in, `DevIdentity:UaePass`, off by
    default. Client ID and secret have no defaults and live only in user secrets, environment variables or `.env`.
    Every UAE PASS endpoint must be `https` on exactly `stg-id.uaepass.ae` (default port): production `id.uaepass.ae`
    and every other host are refused at start-up. The callback is fixed at `{Issuer}/uaepass/callback`.
  - An authorization request with `idp_hint=uae_pass` (or the persona page's "Continue with UAE PASS (staging)"
    button) is validated as before, kept in memory under a new random upstream `state` (hashed key, single use,
    10 minutes, at most 1000 waiting) and sent to UAE PASS with `scope`, `acr_values` (web, level low) and
    `ui_locales` `ar` or `en`. Disabled: `temporarily_unavailable` to the client.
  - The callback consumes the state (unknown, expired or reused: the refusal page, no redirect). A UAE PASS error
    returns `access_denied`; otherwise the code is exchanged (HTTP Basic, 15-second timeout, 64 KiB answers, no
    redirects, no request logging) and userinfo read. Any failure returns `temporarily_unavailable` with a fixed
    description and logs a fixed reason code only. UAE PASS's tokens are dropped at once.
  - The identity is subject `uaepass-stg|{uuid}`, role `participant`. The access token is unchanged (no personal
    claims). The ID token adds `name`, `given_name`, `family_name`, `gender` (`male`/`female`) and an
    `azwah_profile` object (`source: "uae_pass_staging"`, Arabic names, nationalities, `user_type`,
    `emirates_id_masked` such as `784-••••-•••••42-1`, `mobile_masked`, `email_masked`); absent values are omitted. The
    full Emirates ID, mobile number and e-mail address never leave the provider. UAE PASS documents no date of birth in
    userinfo, so there is no `birthdate` **(engineering call: masking formats)**.
  - Refresh responses now include a new ID token (no `nonce`, original `auth_time`, OIDC Core 12.2) for every grant,
    so an app holding only a refresh token after a restart gets the profile back; both apps already accept it.
- **Consequences:** Tested against a stubbed UAE PASS only (no test reaches the network). Tried live: the provider
  redirects to the staging authorize endpoint with the sandbox client and the staging login page loads; the staging
  token endpoint answers a made-up code with `invalid_grant`. **A full staging sign-in has not been completed**: it
  needs a UAE PASS staging app account on a phone (upgraded to SOP3), which was not available. The userinfo field
  names follow UAE PASS's published examples. This is not a production onboarding: production UAE PASS is still not
  configured (open questions 5, 37), and every token stays `azwah_development_only`.

## D-97 Registration: people confirm their UAE PASS details once; the platform stores none of them

- **Context:** After a first UAE PASS sign-in the app shows the person what UAE PASS released and asks them to confirm
  it is them, once. The platform must know that this happened, without collecting the details (D-85, open question
  19).
- **Decision:**
  - `POST /api/v1/me/registration`, Participant policy, `participant-writes` rate limit, body
    `{"detailsConfirmed": true}` (anything else is 400 keyed `detailsConfirmed`). It creates the caller's
    `PlatformUser` (subject and time only, the same path as household creation, D-47) and audits
    `identity.user.registered` (resource type `platform_user`, no free text): 201 `{"registeredAt": …}` when created,
    200 with the first time when already registered, 409 `persistence.conflict` if another request registered the
    caller at the same moment (asking again answers 200).
  - `GET /api/v1/me` adds `registeredAt` (the platform user's creation time, UTC, or null). The app skips the
    confirmation step when it is set. A household creator is registered too.
  - No name, Emirates ID, contact detail or other profile field is sent to or stored by the API; the app shows them
    from the ID token only. No migration.
- **Consequences:** "Registered" means "a platform user exists", whichever came first. Staff roles cannot register.
  Storing any detail later needs its own decision and lawful basis (open question 19).

## D-98 Mobile opening, UAE PASS sign-in and onboarding flow

- **Context:** The owner asked for a real app flow: an opening "Welcome to Azwah" sequence, sign-up or sign-in with
  UAE PASS, a one-time confirmation of the details UAE PASS released for new people (returning people skip it), then
  Home, keeping the questionnaire and scoring work. Screens were designed in Google Stitch with the brand assets
  (`docs/design/mobile-onboarding/`).
- **Decision:**
  - **Opening:** after the native splash, a two-scene sequence over the first screen (`BrandSplash`): "Welcome to
    Azwah" with the emblem in a turning gold ring and a progress line, then the wordmark with the meaning and the four
    pillars. It lasts about two seconds, waits only until the sign-in state is known (the anchor route shows the
    registration check, so a slow server never holds the splash over the lock screen), plays once per start (not after
    the language reload), and only fades with reduced motion.
  - **Welcome:** brand hero, the four pillars with the logo-sheet artwork, and a "Get started" sheet with "Sign in with
    UAE PASS" and "Create an account with UAE PASS". Both run the same UAE PASS sign-in; the server decides who still
    confirms details (D-97). The app names UAE PASS only when the build sends its issuer there (`EXPO_PUBLIC_OIDC_IDP_HINT`)
    or simulates it; any other issuer gets a plain "Sign in", and a profile from an unrecognised source gets a neutral
    badge, never "From UAE PASS" (CONTRIBUTING.md rule 8). The button is drawn in UAE PASS black and white with a fingerprint glyph until the
    official artwork comes with onboarding (open question 5). While the browser is open and tokens are exchanged, a
    "Connecting to UAE PASS" overlay shows (not a native modal, so the system browser always presents above it).
  - **Sign-in request:** `idp_hint` from `EXPO_PUBLIC_OIDC_IDP_HINT` (`uae_pass` for the development provider) and
    `ui_locales` from the app language, on UAE PASS requests only. The ID token's profile claims are parsed with Zod
    into an `IdentityProfile` kept in memory with the session (never in the keystore, SQLite, settings or logs) and
    returned by the first renewal after a restart.
  - **Onboarding:** a guarded `(onboarding)` route group (`/confirm-details`, `/account-ready`) open while `me` says
    `registeredAt: null`, or while "account ready" shows; `(app)` is closed meanwhile. The registration query does not
    run while the app is locked. Offline or failing, the app goes Home (the server needs no registration to serve
    anything). "These aren't my details" explains and signs out.
  - **Development fixtures:** the buttons open a labelled simulated UAE PASS (`/auth/uae-pass`) with two made-up
    accounts (returning, first-time) carrying invented, already-masked details marked `simulated`.
  - Home greets by given name and time of day; the profile shows the name and "Signed in with UAE PASS". The app icons
    are now the Azwah emblem (they were Expo's defaults).
- **Consequences:** The questionnaire, sessions and scoring flows are unchanged and reached from Home as before.
  Production still needs a production identity provider that federates UAE PASS and releases the same claims, and the
  official UAE PASS button artwork. The Home design is a placeholder until the owner's brief (the Stitch Home draft
  carried invented claims and was not kept). Not yet tried on a device with a real UAE PASS staging account.

## D-99 The household assessment enters the platform as a draft with its scoring proposed

*(Numbered D-76 on `main` until the security hardening branch, which had its own D-76, was merged.)*

- **Context:** The 39-item household assessment existed only in the investor demo (`apps/demo-web`), with the brief's
  scoring (reverse-scored items, a 39–156 total, three bands) implemented there. The platform seeded a synthetic
  `[DEV SAMPLE]` instrument, and the mobile fixtures showed a `[DEV PLACEHOLDER]` questionnaire. The owner needs to
  show stakeholders the real questions and the scoring engine (2026-10-19).
- **Decision:**
  - The demo's instrument is copied verbatim into
    `backend/FamilyResilience.Infrastructure/Content/household-assessment.v1.json` (sections coded by area, items
    `Q01`–`Q39`, directions and categories from the demo, English and Arabic) and the brief's scoring into
    `household-assessment.scoring.json` (`summed_item_values` v1, `mirror_scale`, bands `vulnerable`, `transitional`,
    `flourishing`). The development seed imports them as draft version 1 of `household_assessment` and the Proposed
    rule set `household_assessment_total` v1, replacing `[DEV SAMPLE]`.
  - The mobile development fixtures serve copies of the platform's content files (the household assessment and the
    codebook's Module A), kept equal to the backend files by a test, and simulate the proposed rule set's arithmetic
    inside `src/dev-fixtures/` only, labelled on every screen by the development banner. The production client path
    computes nothing.
  - Nothing is approved. The demo's per-item risk flags and area descriptions are not imported: the content model has
    no field for them.
- **Consequences:** The questions and a result can be shown in fixtures mode without activating unapproved content.
  The real API still assigns and scores nothing until a named approver acts on the version and on the rule set (open
  questions 1 and 2). Changing a statement or a band means a new content file, a new draft and a new proposal, never an
  edit of the committed file.

## D-100 Home leads with the family's path and the support routed to it

Superseded in part by D-106 (Profile becomes Settings, in the side menu).

- **Context:** On 2026-10-28 the owner asked for a Home that mostly shows, as visuals, the steps the family or its
  members take to improve their resilience or get help, explains how the process goes, and has the steps "decided by
  the AI engine in the backend". No AI engine exists, no AI provider is approved (D-94), and CONTRIBUTING.md rule 7 forbids
  AI scoring or eligibility. Asked, the owner chose
  approved rules now and AI later. Design: `superpowers/specs/2026-10-28-mobile-home-path-design.md`; drafts in
  `design/mobile-home/`.
- **Decision:**
  - Home leads with "Your family's path": five steps (set up the household, give consent, complete the check-in, see
    the family's next steps, the next check-in in about six months), each done, current or later. `buildFamilyPath`
    orders what the server says (roster confirmation, the caller's member's active participation consent, open and
    completed assignments, routed recommendations); the first step not done is current and carries its action. It
    decides no status, assignment or recommendation.
  - "Your next steps" lists `GET /api/v1/me/recommendations` (D-53) newest first, as recorded: the category code (no
    family-facing wording is approved, open question 13) and the referral status when there is one. The owner's choice
    approves showing them on Home; the result screen's own flags (D-72) are unchanged and stay off. The app never
    chooses, ranks or explains a step. Until a referral rule set is approved, families see "Your next steps will
    appear here after your check-in."
  - An AI-assisted plan engine, if approved later (open question 44), is a backend change that feeds the same section
    and never scores or decides eligibility.
  - Household, past questionnaires and help stay one tap away under "More"; Profile stays in the header.
- **Consequences:** The Stitch draft's invented step titles, icons, "Step 1/2/3" ordering and "12 of 39 answered"
  progress bar were not built (the assignment list carries no answer counts). Category wording and icons wait for open
  question 13.

## D-101 "Ask Azwah": the assistant screen ships switched off

Superseded in part by D-106 (the screen is titled "Azwah assistant" and is a place in the side menu) and by D-108 (the
chat and its endpoint are built; only the model is missing).

- **Context:** The brief asks for an AI assistant chatbot button at the bottom right of Home. No assistant service is
  connected and none is approved; CONTRIBUTING.md rule 8 forbids presenting an integration that is not configured. The owner
  chose to build the chat screen, marked as not switched on.
- **Decision:**
  - An "Ask Azwah" button floats at the bottom end of Home (bottom left in Arabic) and opens `/assistant`: a "Not
    switched on yet" notice with a link to Help, what the assistant will and will not do (general guidance; it will not
    see answers, give scores or decide anything about the family), "In an emergency, call 999" with a link to the
    emergency numbers, example questions as plain text, and a disabled composer with a warning not to share personal
    details.
  - The app makes no request for it and there is no endpoint. Connecting one needs the prerequisites of D-94 and an
    answer to open question 44: the provider and region, a data-processing agreement, what the model may see (never
    answers or scores), safety handling (emergencies send people to `/emergency`), logging (no request or response
    bodies, rule 3), rate limits and evaluation in Arabic and English.
- **Consequences:** The button is a promise of a feature, and the screen says plainly that it is not working yet.


## D-102 SOS: UAE emergency and family-protection numbers in the app

- **Context:** The brief asks for an SOS button at the bottom left of Home that lists the UAE emergency and helpline
  numbers for the family and opens the phone's keypad with the number filled in. The owner chose the nationwide numbers
  plus each emirate's family-protection lines, each verified on an official site (2026-10-28).
- **Decision:**
  - A red "SOS" button floats at the bottom start of Home (bottom right in Arabic, as start and end follow the
    language) and opens `/emergency` as a modal: an "In danger now?" notice with "Dial 999", then three groups.
    **Emergencies:** police 999, ambulance 998, fire and civil defence 997. **Family and wellbeing support (whole
    UAE):** the Ministry of Interior's Child Protection Centre 116111 (for reports that are not emergencies; the row
    says to call 999 for a child in danger now) and Ewaa shelters 800 7283. **In your emirate:** Dubai Foundation for
    Women and Children 800 111 (Dubai, 24 hours), Family Care Authority 800 444 (Abu Dhabi), 800 SAKINA mental health
    line 800 725462 (Abu Dhabi Department of Health, 24 hours), Child and Family Protection Centre 800 700 (Sharjah),
    Hemaya Foundation 800 446292 (Ajman), Aman Centre for Women and Children 07 235 6666 (through Ras Al Khaimah
    Police). Fujairah and Umm Al Quwain list no line of their own; the footnote sends them to 999 or 116111.
  - Sources: the u.ae emergency page (moved to `…/Safety/handling-emergencies`, updated 2026-10-01), the Child
    Protection Centre and the Dubai Foundation's own sites, and Abu Dhabi Media Office releases. Each entry in
    `src/features/emergency/emergencyNumbers.ts` records its source and check date.
  - Left out: 800 4673 (800 HOPE, the former national mental support line; its official site no longer exists and the
    current u.ae pages do not list it), Dubai CDA 800 988 (u.ae lists it for child and women protection, the authority's
    own site for addiction counselling), numbers only the Ministry of Community Empowerment's directory carries (it has
    at least one wrong number), lines whose English and Arabic u.ae pages disagree, and the ministry's office-hours
    counselling line 800 623 (not a crisis line).
  - The list lives in the app, not the API, so it works offline and before anything loads. A tap opens the dialler
    through a `tel:` link with the number filled in; nothing is called automatically (iOS asks to confirm). A device
    without a dialler shows the number to dial by hand. 800 numbers work only inside the UAE.
- **Consequences:** The numbers need an owner and a review cycle (open question 45); the code asks for a review at
  least every six months. Only signed-in people reach the list today, from Home.

## D-103 The family's final score: total and band, shown to the head of household only

- **Context:** On 2026-10-28 the owner said the final score of the family resilience survey (the 39-item household
  assessment) can be seen only by the head of household, and also in the admin dashboard (D-104). Until now no family
  member saw any score (D-72's display flags were all off). Asked, the owner chose that the head sees the total score
  and its band, which approves the proposed cut-offs (39–78, 79–117, 118–156) and shows the band names as the
  specification words them; and that only the head, signed in to their own account, sees it.
- **Decision:**
  - The server decides it. `GET /api/v1/sessions/{id}/result` returns `scoreVisibility`: `visible` for a caller whose
    own linked member is the household's active head (`Household.IsHeadOfHousehold`), otherwise
    `head_of_household_only` with no total, tier or category scores. `GET /api/v1/me/assessment-history` leaves the
    tier out the same way. A manager answering for a head without an account sees the state, not the score.
  - It applies to every questionnaire's scores, not only the household assessment: the family-voices modules have no
    approved scoring today, and a member's score shown to someone else would need its own decision.
  - The app turns on `score` and `tier` in `RESULT_DISCLOSURE` and shows them exactly as returned, with the band named
    in the reader's language (Vulnerable / هشّ, Transitional / انتقالي, Flourishing / مزدهر, from the specification;
    any other code is shown as recorded). Area scores and support categories stay hidden: no wording for them is
    approved (open questions 3 and 13). Anyone else sees "Your family's final score is shown to the head of household
    only."
- **Consequences:** Closes open question 2 for the bands. No family sees a score yet: the household assessment is a
  draft version and `household_assessment_total` v1 a proposed rule set (D-99), and only approved ones run. Approving
  them in the platform is a system administrator's action with a recorded approval reference. Supersedes D-72's "all
  off" for the total and the band. What a band means for a family is still not written (open question 3's wording).

## D-104 Household results in the admin dashboard, for system administrators

- **Context:** The owner asked that the final score also be available in the admin dashboard (2026-10-28). Programme
  administrators never see scores (D-62); researchers see suppressed distributions and pseudonymised exports only.
  System administrators can already read any session's score results through the API.
- **Decision:** `GET /api/v1/admin/results` (system administrators only, permission `results.read`, rate-limited as a
  staff read, every read audited as `scoring.household_results.read`) lists each scored session's latest result with
  the household reference, wave, questionnaire, state, total and tier, newest first, filtered by wave, instrument code
  and household reference. No answers, members or category scores. The dashboard's "Household results" page shows it,
  naming the bands in English and Arabic and every other value as recorded. The wave list moves to a new `WaveReader`
  policy (researchers, programme administrators and system administrators) so the page's wave filter works; waves
  hold no personal data.
- **Consequences:** No staff role gains a score it could not already read. Who may hold the system administrator role
  stays open question 38.

## D-105 The family survey leads Home; the SOS list can be searched

- **Context:** The owner said that after the household is set up, the family survey is what matters most: it gives
  the data the family's next steps are decided from. They also asked for a search bar on the SOS screen, because the
  list is long and will grow (2026-10-28).
- **Decision:**
  - Home calls the check-in step "Answer the family survey" and says it is the most important step, because the
    family's next steps are based on its answers. The next steps still come from approved referral rules (D-100); the
    wording names no AI.
  - The SOS screen has a search field below "Dial 999". It matches a service's name, description or emirate in either
    language (Arabic without diacritics, tatweel or alef and taa marbuta variants), or its number typed with spaces or
    Arabic-Indic digits; every word must match. Groups with no match are hidden, the number found is announced, and
    when nothing matches the screen says so and points to 999. The search runs on the phone, so it works offline.
- **Consequences:** None for the server. New entries are found by search once their names are translated.

## D-106 The side menu: the app's main places

- **Context:** On 2026-10-29 the owner asked for a side menu as the app's overall layout, listing Home, Azwah assistant
  (an AI chatbot), Azwah consultant (a speech-to-speech AI consultant), Your household, Your family resilience, All
  services and Settings, with sign out at the bottom.
- **Decision:**
  - The signed-in screens are in two layers. The main places sit side by side in a `(drawer)` group under Expo
    Router's Drawer; everything opened from them (household, questionnaires, results, help, consent) stacks on top with
    a back button; SOS stays a modal. The drawer needs `react-native-gesture-handler` (~2.32.0, the SDK 57 version)
    and a gesture root at the app's root.
  - The menu lists the places in the owner's order. Sign out sits at the bottom, apart, and asks first (the same
    dialog as Settings). The menu shows the signed-in name and household reference, marks the open place with a bar
    and a tint (never colour alone), and labels the assistant and the consultant "Not switched on yet".
  - It comes in from the start edge (from the right in Arabic in a development build; Expo Go keeps left to right),
    opens from the menu button that replaces back in each place's header, and closes on back, a tap outside it, a
    swipe or its close button. Back from a place returns to Home.
  - Where each item leads: Home (`/home`); Azwah assistant (`/assistant`, D-101, now titled "Azwah assistant"; Home's
    floating button still says "Ask Azwah"); Azwah consultant (`/consultant`, D-107); Your household (the household
    screen, or the set-up screen when there is none); Your family resilience (`/resilience`: the questionnaires still to
    answer, past questionnaires, each opening its result, where only the head of household sees the score and band
    (D-103), and the next steps); All services (`/services`: the searchable list of D-102 and D-105 as a place of its
    own); Settings (`/settings`, replacing Profile: account, language, consent, help, the person's data and sign out).
  - The item says "Sign out", the app's wording everywhere, where the owner wrote "logout" **(engineering call)**.
  - Home's header loses its Profile link (D-100). Its "More" links and the floating SOS and "Ask Azwah" buttons stay.
- **Consequences:** All services lists only the numbers in the app; the approved support-resource directory (D-92) can
  join it once it has content (open question 41) and a mobile screen. A link straight to a place or a household still
  has Home beneath it. Jest runs the real drawer with the stand-ins gesture-handler, Reanimated and Worklets ship for
  tests (`apps/mobile/jest.setup.ts`).

## D-107 The Azwah consultant: a voice screen that ships switched off

Superseded by D-109 (the live conversation and its endpoint are built; only the model is missing).

- **Context:** The owner asked for "Azwah consultant", a speech-to-speech AI consultant, in the side menu
  (2026-10-29). No voice or AI service is connected or approved (D-94, open question 44), and CONTRIBUTING.md rule 8 forbids
  presenting an integration that is not configured.
- **Decision:**
  - `/consultant` shows a "Not switched on yet" notice with a link to Help; what the consultant will do (talk out loud
    in Arabic or English, with general guidance about family wellbeing and using the app) and will not do (see answers,
    give scores or decide anything about the family); a disabled "Start talking" button; and "In an emergency, call
    999" with the emergency numbers.
  - The app asks for no microphone permission, records nothing and sends nothing. There is no endpoint.
  - Switching it on needs the prerequisites of D-94 and an answer to open question 46.
- **Consequences:** Like the assistant, the menu item is a promise of a feature, and the screen says plainly that it is
  not working yet.

## D-108 The Azwah assistant: a working chat, kept on the phone, with everything about the family

- **Context:** On 2026-10-29 the owner asked for the AI screens and their backends, leaving out only the model, whose
  endpoint they will supply. Asked, they chose: everything local for this MVP (the server stores no conversation), and
  the AI sees "everything about the family and the user". Design:
  `superpowers/specs/2026-10-29-ai-screens-backends-design.md`.
- **Decision:**
  - A new backend module, `Assistant`, owns no table. `FamilyContextBuilder` builds the context on the server for the
    signed-in caller from the platform's records, through other modules' interfaces: the caller's relationship and
    roles; the household and the members the caller may act for (D-49: relationship, age, employment, head and
    main-earner flags); consents; questionnaires; every answer as its question and chosen option in the person's
    language; next steps; and the family's score and band only for the head of household's own account (D-103). No
    names, UAE PASS details, tokens or IDs. Nothing about the family is taken from the phone.
  - `GET /api/v1/assistant/availability` and `POST /api/v1/assistant/replies` (policy `Participant`, rate limit
    `assistant`, 20 a minute; permission `assistant.use`). The request carries the language and the latest messages
    (at most 40, 4,000 characters each, 20,000 in all, the last the person's); the server adds the context, asks
    `IAssistantModel` and returns the reply. Without a model: 503 `assistant.unavailable`; a model that fails, answers
    nothing or takes over 30 s: 502 `assistant.model_failed`. Each call that sends the context to a model is audited as
    `assistant.reply.generated`, succeeded or failed, with no content. Messages, replies and the context are never
    stored or logged.
  - The model port is `IAssistantModel`; `UnconfiguredAssistantModel` is registered until the owner's endpoint is
    wired as an adapter (settings under `Assistant:Model`, secrets in user secrets). OpenAI is wired in D-119.
  - The app keeps the conversation on the phone, sealed like drafts (SQLite table `assistant_messages`, migration 4),
    at most 200 messages, and sends the latest 20 within the server's limits (a long reply is cut, the oldest messages
    dropped). It is deleted at sign-out and when another account signs in,
    before the drafts (whose clearing deletes the shared key). Without a working keystore it stays in memory and the
    screen says so. "Clear chat" asks first.
  - The screen says what the assistant uses to answer and that it gives no scores or decisions, shows the replies as
    the model wrote them, and states its problems in translated words (not connected, offline, too many questions,
    failed with retry). Development fixtures answer with a reply marked as simulated.
- **Consequences:** Family data, answers included, will leave the platform for the model's provider once an adapter
  is wired: that needs the provider, region, data-processing terms and a decision on consent (open question 47; see
  44). The model must never score, band or decide eligibility (rule 7); its replies are guidance shown to the person
  and nothing in the platform acts on them.

## D-109 The Azwah consultant: a live spoken conversation, relayed by the API

*Protocol version 2 and the phone's audio are in D-124 and D-125 (2026-11-09).*

- **Context:** The owner chose a live conversation (talk freely and interrupt, like a call) over push to talk, with the
  same context as the assistant (D-108), on 2026-10-29.
- **Decision:**
  - `GET /api/v1/consultant/live?language=` is a WebSocket (policy `Participant`, rate limit `consultant`, 6 starts a
    minute; the bearer token in the `Authorization` header, never in the URL). Protocol version 1: the app sends 16-bit
    mono PCM at 16 kHz and `{"type":"end"}`; the server sends `ready` (with both sample rates), the consultant's PCM,
    `transcript` lines for both speakers, `turn_end`, `interrupted`, and `error` with a fixed code. With no model
    connected it sends `unavailable` and closes. One live session per subject (a second start closes the first,
    reason `replaced`), 15 minutes at most (`too_long`), closed after 60 seconds without audio (`idle`), frames of at
    most 64 KB (close 1009); opening a model session has the same 30-second limit as a reply. Opening one is audited as
    `assistant.consultant.started`, succeeded or failed; no audio or transcript is kept. The upgrade has no Origin
    check: sign-in is a bearer header a browser cannot send, and React Native on Android sends its own Origin
    **(engineering call)**.
  - The model port is `IVoiceConsultantModel` (a session that takes audio and yields audio, transcript, turn-end and
    interrupted events); `UnconfiguredVoiceConsultantModel` until the owner's endpoint is wired. OpenAI is wired in
    D-120.
  - The app records with expo-audio's `useAudioStream` (works in Expo Go on SDK 57), resampled to the server's rate,
    and plays the reply as half-second WAV segments in order. While the reply plays, the microphone is not sent unless
    the person clearly talks over it, which stops the reply. Mute and End are always there; leaving the screen or the
    app locking ends the call. Captions are kept in memory only.
  - Microphone permission is asked when the person presses Start talking (iOS text in `app.json`; background audio
    is off). Refused, the screen explains and links to the phone's settings.
  - Development fixtures play back what the person said after a pause, with captions marked as simulated, so the
    microphone and playback can be tried without a model.
- **Consequences:** Speech is sensitive personal data; the provider, region, retention of audio and transcripts on
  the provider's side, and the microphone consent wording are open (open questions 46 and 47). Echo is handled only
  by not sending the microphone while the reply plays; a headset gives the best barge-in.

## D-110 All services lists the approved support directory

- **Context:** The owner asked that All services also show the support directory administrators manage (D-92), so
  services can be added without an app update (2026-10-29).
- **Decision:**
  - `GET /api/v1/support-resources` (policy `SupportResourceReader`, paged as `request`) lists the approved entries
    that search could find (approved, before their review date, visible to the caller's roles), ordered by code.
  - All services shows the in-app numbers (D-102), then the directory: the whole list, or, once the person has typed
    two characters and paused, the server's own search (D-93) for the same text. Each entry shows its name, provider
    and summary in the person's language and opens its `https` link in the in-app browser. The section says it is
    information only and that Azwah does not refer people to these organisations **(engineering wording; open
    question 43)**. SOS still opens the numbers alone.
  - Development fixtures hold three synthetic entries marked as placeholders.
- **Consequences:** The directory stays empty until entries are approved (open question 41).

## D-111 The grading scale on a result

- **Context:** On 2026-11-05 the owner asked that the result also display the grading scale and show where the score
  stands. The owner approved the household assessment's bands in D-103; clients may not hold scoring rules (rule 1).
- **Decision:**
  - `SessionResultResponse` gains `scale`: the lowest and highest possible totals and the bands, read by
    `ScoreScaleReader` from the rule set that recorded the result, so a scale is always the one the score was computed
    on. It is sent only to the household's head (D-103) and only with a complete result; otherwise it is null. A rule
    set without bands has no scale, and the result then shows as it did.
  - The app draws what it is sent (`ScoreScale`): a bar with each band as wide as its number of totals, in one hue from
    light to dark (status colours stay for status), lowest band at the start in either language, each band's name and
    range listed, and the recorded band labelled "Your family: 96" so that colour is never the only cue. A screen
    reader hears it as a sentence.
  - The marker's place is geometry (`scalePosition`), and the highlighted band is the server's `tierCode`: the app never
    works out a band.
- **Consequences:** No band meanings are written or implied (open question 3). Area scores stay hidden as in D-103.

## D-112 The written score report, by an OpenAI model

- **Context:** On 2026-11-05 the owner asked for a model to evaluate the score and write a downloadable report with
  strengths, weaknesses, where the family can improve and how, using an OpenAI API key they will supply. Rule 7 forbids
  AI scoring and open question 47 leaves provider, region, terms and consent open. The owner's earlier choice that AI
  features are local to the phone and may see the family's data (D-108) was carried over; the rest below are
  **engineering calls** for the owner to check.
- **Decision:**
  - `POST /api/v1/sessions/{id}/report` (policy `SessionParticipant`, rate limit `score-reports`, 10 an hour, because
    each call is paid and sends answers out) is for the household's head only (403
    `assistant.score_report_head_only`), for a session with a complete score on a graded scale (409
    `assistant.score_report_not_ready`). Without a model it answers 503 `assistant.unavailable`; a model that fails, writes
    nothing usable or takes over 90 s, 502 `assistant.model_failed`.
  - **The model explains and decides nothing.** The score is computed before it is asked. The report's total, band and
    scale come from the record; the model's output type has text fields only, and its instructions
    (`ScoreReportPrompt`, tested) forbid recalculating or disputing a score, diagnosing, naming organisations or
    numbers, and promising support. Nothing in the platform reads the text.
  - **It sends only what the report needs:** the total, band, scale, area scores, and each question with its answer and
    points. Unlike the assistant's context it has no roster, no ages, no consents and no household reference, and no
    names or IDs. Its questions and answers are instrument content, so no free text from a person reaches the model.
  - **A person asks for it.** The result screen draws the scale at once, but the report waits for "Create my report",
    below a plain statement of what goes to OpenAI. Opening a screen sends nothing **(engineering call: the alternative,
    writing it automatically, would send every family's answers out unasked and pay for reports nobody reads)**.
  - **Storage:** the server keeps neither the facts nor the report; every send is audited as
    `assistant.score_report.generated` (the session, the outcome, no text), also when the person leaves while the model
    writes. The phone seals the report like drafts (`score_reports`, phone migration 6 since the merge with D-123, 5 on its branch), one per session and language, deleted
    at sign-out and when another account signs in. So a report opens again without a new paid call.
  - **Adapter:** `OpenAiScoreReportModel` calls the Responses API at a fixed address with `store: false`, a strict JSON
    schema and low reasoning effort. The default model was `gpt-6.1-terra`, which OpenAI lists for writing, and is now
    `gpt-5.4-mini` (D-116) **(engineering call; change it with `Assistant:Model:ReportName`)**. The key is `Assistant:Model:ApiKey`, in the deployment's secret store
    or user secrets, or locally `OPENAI_API_KEY` in the git-ignored `.env`, as the owner chose on 2026-11-05 (Compose
    passes it on; `LocalEnvFile` reads that one variable in Development only, because `dotnet run` does not read
    `.env`). It never reaches the phone: the variable has no `EXPO_PUBLIC_` prefix and Expo reads only `apps/mobile/.env`. Without it the feature says it is off. It logs a fixed
    code only, never OpenAI's messages. (The assistant and the consultant were connected later, D-119.)
  - **A deployed API refuses to start** with a key and no `Assistant:Model:ApprovalReference`
    (`assistant_model_not_approved`, shared with D-119), so answers go to OpenAI from production only after legal review is on record.
  - **Download:** "Download report (PDF)" prints the report on the phone from a self-contained page: every value escaped
    and a content policy that forbids outside resources, so a model's text cannot add markup. On Android the person
    chooses a folder, starting at Documents because Android refuses the storage root (share sheets often cannot keep a
    file there); on iOS the share sheet offers "Save to Files". The Android path was tried on an emulator (Android 16),
    which found that the chooser answers just before the app is active again; the iOS path has only been tested with
    fakes. The
    printer's own copy and any temporary file are deleted, the file name holds only the date, the app warns that the file
    is unprotected, and it does not open while locked. It works offline.
  - The report is labelled as written by an AI model, says it may contain mistakes and changes nothing, and points to 999.
- **Superseded in part by D-113:** the report is now normally written as the person answers. "Create my report" remains
  for a session without that.
- **Consequences:** Closes open question 47 for development use only (see 48). A real family's report needs the
  household assessment and its scoring rule set approved in the platform (D-99, D-103). OpenAI may keep requests for its
  own abuse monitoring whatever `store` says, and the disclosure says so. The wording and the model's output have had no
  expert review. Development fixtures answer with a report marked as simulated. D-103's "what a band means is not
  written" still holds for the app's own words; the model's text is labelled as its own.

## D-113 The report is written as the person answers, and ready at once when they finish

- **Context:** On 2026-11-05 the owner found a whole report's 14 seconds too long, and asked that answers go to the AI in
  real time as they are recorded, and that the report take no more than 2 seconds once the questionnaire is done and be
  ready for download. Measured live: the smallest model call takes 2.0 to 5.8 seconds whatever the model or effort
  (`gpt-6-luna` and `gpt-6.1-terra`, reasoning `none` and `low`), so no call at the end can meet 2 seconds.
- **Decision:**
  - **No model call happens at the end.** Everything is written before the person submits; on submitting, the phone
    joins the finished words with the score the server just recorded (`assembleReport`). Measured with the real 39
    questions, real scoring and the real model: 0.08 to 0.43 seconds from submit to result, on a local server.
  - **Answers go a section at a time, not one by one (engineering call).** When a section is fully answered and saved,
    `POST /sessions/{id}/report/parts/{sectionCode}` writes that section; when all are, `POST .../summary` runs alongside
    the last one. The model keeps no memory between calls (`store: false`), so sending each answer would be 39 paid calls
    that each resend everything, and would not be faster at the end. A changed answer rewrites only its section, at most
    one extra call however often it changes.
  - **The person agrees first (engineering call).** A card at the top of the questionnaire says the answers go to OpenAI
    as each part is finished, even if they do not finish, and asks. Ignoring it is no. It can be stopped at any time,
    which deletes what was written. Without agreeing, "Create my report" works as before. This replaces D-112's tap at the
    end, because the answers now leave earlier.
  - **It applies only to** the household's head (D-103), with active participation consent, on a session an approved
    scoring rule set covers (`GET .../report/eligibility`, sending nothing): rule 9 holds, so with nothing approved nothing
    is ever sent. The answers come from the server's records, never from the phone, and points use the score's own rule
    (`ScoringItem.PointsFor`). The model receives no total or band, because none exists yet, and the report's come from
    the record.
  - **Storage:** the server still keeps nothing; each send is audited without text (`assistant.score_report_part.generated`,
    `assistant.score_report_summary.generated`). The phone seals the parts like drafts (`score_report_parts`, phone migration 7 since the merge, 6 on its branch)
    and deletes them once the report holds their words. Rate limit `score-report-parts`, 120 an hour.
  - **Failures:** a dropped connection is tried again at both ends (seen on about 3 calls in 60 live). Anything still
    missing at the end (answered offline, submitted the instant the last call began, a model failure) is written then, and
    the screen says so; if that fails it offers "Create my report". So 2 seconds holds when the parts are finished, which
    took 4.5 to 5.6 seconds after the last answer in live runs.
  - Parts run with no reasoning step, which was measured faster on short text. Choosing a model that refuses reasoning
    `none` (`gpt-6-astra`, `gpt-6.1-sol`) in `Assistant:Model:ReportName` would fail these calls.
- **Consequences:** Answers leave the platform before the person submits, including answers they later change or never
  submit: stricter than D-112, and part of open question 48. About 8 model calls a report instead of 1, each far shorter.
  A report is built section by section, so it reads as a list by area and may repeat itself a little across areas, and
  holds up to one strength, one weakness and one improvement for each of the seven areas. Testing on the server used a
  disposable database with its content activated for the run only; no local or shared database was changed.
- **Measured on the phone, and what is not yet shown:** on a low-memory Android 16 emulator in Expo Go, in debug mode with
  development fixtures, the whole path worked and took 3.16 seconds from the tap to the report on screen, of which about
  0.7 is the fixtures' simulated network and 0.14 is joining the parts and showing them. The rest is the existing
  submission path and debug-mode rendering. **The 2-second target is therefore met on the server (0.08 to 0.43 seconds) but
  not yet shown on a phone:** that needs a release build on a real device against a real API, because fixtures run only in
  debug builds. The final save check before submitting took 0.88 seconds with nothing to save; it was left alone because
  it guards against losing answers, and is the first place to look if a release build still misses the target.

## D-114 A family's next steps: a flow, with the authorities and procedures people approve

- **Context:** On 2026-11-05 the owner asked that the report create a flow for the person: the next steps to improve their
  resilience, the authorities and ministries to go to, and the procedure for the whole process. They said the ministries
  will be added after this work. Rules 7 and 9 and threat T-22 bear on it: a made-up ministry or procedure would send a
  family to the wrong place, and the support directory (D-92) can never be linked to answers and holds no procedures.
- **Decision:**
  - The report's improvements become **"Your next steps"**: numbered steps, the area that needs most attention first. Each
    says where and why, what to start at home (the model's words), then where to go and the procedure there.
  - **No model names an authority or writes a procedure (engineering call).** They come word for word from a catalogue
    people write, `support-authorities.v1.json` (replaced by the knowledge graph, D-115).
    The catalogue is never sent to the model. Which authorities a step lists is decided by the section it is about,
    from a list people approved, not by the model; the model only decides whether an area has an improvement at all, so
    a strong area lists none.
  - **The catalogue ships empty,** and a step then says its authorities haven't been added yet. Every entry needs an approval
    reference, Arabic and English for every text, an `https` website if any, and a review date; an entry past it leaves new
    reports, and a mistake in any entry stops the API from starting. It is a file for now, as the owner will supply the
    list **(engineering call: an admin screen can follow if people outside engineering must edit it)**.
  - **Order comes from the server:** `standing` says where an area's points sit between its lowest and highest possible.
    The phone sorts by it and never shows it, as area scores stay hidden (D-103). It sets an order only: no threshold, so
    nothing was invented.
  - The whole-report call asks the model for each improvement's section code; a code it gets wrong means that step lists no
    authority and sorts last, because an authority is never guessed.
  - It works within D-113: authorities arrive with each part, so the end still calls no model.
  - The step and the PDF say Azwah contacts no authority and shares nothing with one, that the person goes themselves, and
    that the authority decides what applies to them. Each shows when its procedure was checked. Website and phone open the
    in-app browser and the dial pad; nothing is sent or called by itself.
- **Consequences:** This is not a referral (D-61): no consent, hand-off or record. Open question 44's "AI-assisted family
  plan" is answered for the home steps only. Open question 49 asks who owns the catalogue. A report keeps the procedure
  it was written with, so a saved or downloaded one can go out of date; it shows its check date. No authority exists yet,
  so this is tested only with synthetic entries. D-115 replaces the catalogue, the choice by section and the model's
  ignorance of the list; the rule that no model names an authority or writes a procedure stands.

## D-115 The support knowledge graph: 13 organisations, inference over answers, and Azwah's own symbols

- **Context:** On 2026-11-06 the owner named 13 organisations to add, asked for them on the "Other services" page with logos
  Azwah makes up instead of official ones, and asked for research into them and a knowledge graph used for inference when
  generating the report and next steps. That changes D-114, which kept the list away from any model and chose by section.
  Rules 7 and 9 and threat T-26 still bear on it: nothing may be invented, and no model may decide eligibility.
- **Decision:**
  - **One file, `support-knowledge-graph.v1.json`,** replaces the catalogue. Its nodes are questions, needs, services,
    authorities and emirates, and its edges say which question may show which need, which service addresses it, who offers
    it, where, and through whom. Procedures belong to a **service**, because the research showed one authority's services
    differ. How it works and how to change it: [`support-knowledge-graph.md`](support-knowledge-graph.md).
  - **Inference has two stages (engineering call).** First the server walks the graph deterministically from the answers,
    ordering services by the room to improve shown by the answers leading to them, with no threshold. Then the model picks
    from those by code, since the owner asked for the graph to inform the writing and the model can read the answers in context.
    The server drops any code it did not offer and falls back to the graph's order, so a model can narrow the list and can add
    nothing. A model still never sees a procedure, phone or website, and writes no name.
  - **It stays within D-113:** this runs while a part is written, so the end still calls no model.
  - **No procedure is invented.** Where no source publishes one, `procedure` is empty and the app says so. Where a phone or
    site could not be confirmed it is `null`, or the Abu Dhabi contact centre where a page gives that. Each service records
    how its facts were seen (`opened` or `search_extract`) with its sources.
  - **Nothing here counts as approved by the organisations.** The owner named them, and the facts come from public sources on
    2026-11-06. Each entry's approval reference says so, every card tells the person to confirm before going, and open question
    50 lists what to check.
  - **Bodies with no family-facing service stay listed but unreachable (engineering call):** the Sharjah statistics department
    and the Supreme Council have `services: []`, as they appear to take no family's case.
  - **Protection from harm is never inferred (engineering call).** No question asks about it, and guessing from answers about
    disagreement would be wrong and frightening. SOS and All services keep it within reach.
  - **Private providers carry a fee note (engineering call)** and appear only if a model picks one or nothing else was reached,
    so Azwah does not steer families to a business by default.
  - **"Other services" is the All services page,** as no page has the other name **(engineering call)**. It lists the 13 from
    `GET /api/v1/support-authorities`, the same for everyone, searched with the page's field. A family's emirate is unknown, so
    every card says where it serves (open question 51).
  - **Symbols are original (`authorityMarks.ts`):** one pine tile and line style showing what each does, with no text, and a
    note that they are not the organisations' logos. They are plain SVG, so screen and PDF share them; this adds
    `react-native-svg`.
- **Consequences:** Which services were reached says something about a family, and now goes to OpenAI with the answers it
  already sees; the options themselves are public (threat T-26). A part's call has more to read: on real OpenAI with made-up
  answers, English parts took a median of 3.4 and 3.8 s in two runs against 3.2 and 3.3 s without options, Arabic parts up to
  6.2 s, with no failures in 28 calls, every returned code among those offered, and no organisation named in the text. This
  happens in the background, so the last part can still be writing for about 6 s after the last answer (D-113); the final
  step on a real phone remains unmeasured. The directory (D-92) is unchanged: it stays unlinked to answers. The graph is not a referral (D-61). Budgeting and debt advice and the northern emirates have no public service in
  the graph, which the app states rather than filling in. A question-to-need edge is a judgement about help, not a scoring rule,
  and it changes no score.

## D-116 A faster default model for the report, and a part that keeps its first entries

- **Context:** On 2026-11-06 the owner asked to run the whole stack "optimized for latency". On the real API with made-up
  answers, over a phone hotspot (ping about 235 ms), `gpt-6.1-terra` wrote a part in about 4.2 s in English and 5.3 s in Arabic.
- **Decision (engineering call):**
  - **The default model is `gpt-5.4-mini`** (D-112 named `gpt-6.1-terra`): about 2.0 s in English and 2.8 to 3.5 s in Arabic.
    `gpt-6.1-terra` writes slightly better, and stays one setting away (`Assistant:Model:ReportName`). `gpt-5.4-nano` was no faster in
    Arabic, its Arabic had garbled sentences, and it offered a private clinic beside public options, so it was rejected.
  - **A part keeps the first two entries of a list instead of failing** (`ScoreReportPartWriting.Limited`). `gpt-5.4-mini`
    sometimes writes three weaknesses (7 of 103 parts, each a good answer otherwise), which threw away a paid call and cost
    a retry. After the change: 0 failures in 63 parts. A part with nothing usable is still refused.
- **Tried and left out:** keeping connections to OpenAI open between calls. A person's 75-second pause made no measurable
  difference (2.0 to 3.0 s against 1.7 to 3.2 s), so no code was added. OpenAI's faster service tier saves about 0.4 s a call
  at a higher price, and is not used.
- **Measured on the Android emulator (debug bundle in Expo Go, 2 GB, the computer short of memory, real API and model):**
  pressing confirm to the phone holding its result took 2.3 s, of which the submit itself took 0.16 s; about 1.4 s passed on
  the phone before the request left. The last part and summary finished 2.1 to 3.5 s after the last answer. D-113's 2 seconds
  is therefore close here, not shown: a release build on a real phone is still the test that decides it (D-113).
- **Consequence:** The writing rules are unchanged, and in live runs the model named no organisation. One private service was
  chosen among 21 shown in one run, labelled as private, as D-115 allows.
- **Merged with the OpenAI assistant (2026-11-10, engineering call):** the report and D-119's assistant and consultant
  had each built an `OpenAiSettings` and a Responses API sender. They now share one settings record (the report's model
  is `Assistant:Model:ReportName`, default still `gpt-5.4-mini`; `Assistant:Model:Name` is gone), one HttpClient with
  `HappyEyeballs` (D-121), one sender (`OpenAiResponses`) and one start-up problem (`assistant_model_not_approved`). Each
  keeps its retry rule: a dropped connection is tried once more for both; the chat also retries a 429 or 5xx after a
  pause, the report does not. A second dropped connection now comes back as `assistant.model_failed` with a logged
  code instead of an exception, which the report's use cases already turned into the same failure. The session result's
  grading scale (D-111) and the trend chart's scale (D-132) were also built twice: both now send one shape, bands as
  `{code, min, max}` like the rule set itself, from one `ScoreScaleReader`.

## D-118 Mobile UI polish: a heading scale for Markazi, grouped links, quieter notices, tonal press states

- **Context:** A screen-by-screen review of the mobile app (2026-11-08, rendered at phone size) found the hierarchy
  flat: Markazi Text's small Latin x-height made 22 pt headings read no larger than 16 pt Readex body text. It also
  found link lists built from stacks of separate cards, a card nested in a card on Home, notices marked by a thick
  start-edge stripe, presses shown only as an 85% fade, and pillar names on the welcome screen cut short. Numbered
  D-118 because unmerged branches hold D-111 to D-117.
- **Decision:** Presentation only; no behaviour, route, label or content changes.
  - Type scale: display 42/54, title 34/44, heading 26/34, and a new `barTitle` 23/30 for the top bar.
  - `ListGroup` and `ListRow` (one surface, hairline dividers, each row one labelled button) replace stacked pressable
    cards for Home's More, Settings' More and the household's links.
  - `Notice` keeps its tint, glyph and words; its stripe becomes a hairline border in the tone's colour. `Badge` gains
    the same faint edge, so it stays visible on tinted panels.
  - Pressed buttons, cards, options, rows, header icons, menu items and the floating actions change tone (primary to
    ink, outlined to pine tint, surfaces to parchment) instead of fading, so labels keep their contrast.
  - Home's path sits on the page with the current step as its one raised surface; Help's answers read as one page
    divided by hairlines; the assessment facts form two columns; the welcome pillars form a 2 × 2 grid with full
    names; the assistant's way to the emergency numbers is a full-width button; sections leave more space above
    their headings than below.
  - The answer review lists every question on one `ListGroup` (rows take custom content); household start shows its
    steps as a trail like Home's path; radio-card text sits at the layout's start edge, so a label in the other
    script (العربية in English) lines up with its description.
  - Badges stay on one line up to 1.3× system text size: Android measured "تسجيل دخول بالمحاكاة" a hair narrower than
    it drew it and clipped the last word (already so before this change). Their faint edge is drawn over the badge,
    not as a border, so it takes no width from the text.
- **Consequences:** Checked in Arabic and English in an Android development build (2026-11-08): home, menu,
  settings, questionnaire, review, onboarding, household start and form, consent. All new styles use start and end
  only.

## D-119 The Azwah assistant answers with OpenAI's GPT-6.1 Terra

*The model and key stand; the single call became the agent loop of D-122 (2026-11-09).*

- **Context:** On 2026-11-08 the owner asked to connect the assistant (D-108) and the consultant (D-109) to OpenAI with
  the key in the repository's git-ignored `.env`, choosing the best model for each use. (D-111 to D-118 are held by
  unmerged branches, so these decisions start at D-119.) Design:
  `superpowers/specs/2026-11-08-openai-models-design.md`.
- **Decision:**
  - `OpenAiAssistantModel` (Infrastructure) implements `IAssistantModel` over the Responses API
    (`https://api.openai.com/v1/responses`, `store: false`, at most 2,000 output tokens). The instructions are
    `AssistantPrompt.ChatInstructions` (Application, tested): the person's language, plain text, short replies, the
    context is data, never calculate, change or predict a score, band or eligibility, results only for the head of
    household, no diagnosis or professional advice, no invented organisations or numbers (All services lists the
    approved ones), SOS when something the person says suggests danger, and the app named عزوة in Arabic. The family
    context goes as a developer message, as JSON with codes as snake_case text, ahead of the conversation.
  - Model `gpt-6.1-terra` with reasoning effort `low` **(engineering call)**. Measured with the key on 2026-11-08:
    Terra 2.3 to 3.2 s a reply in English and Arabic, with the most natural writing; GPT-6 Luna (cheapest) 3.2 to 3.7 s;
    GPT-6.1 Sol 2.6 to 5.0 s at twice Terra's price; GPT-5.4 mini 1.9 to 2.3 s but a generation older. Terra costs
    US$1 input and US$5 output per million tokens. `Assistant:Model:ChatName` and
    `Assistant:Model:ChatReasoningEffort` change them.
  - The key is `Assistant:Model:ApiKey` (user secrets, or the deployment's secret store). In Development only,
    `dotnet run` also reads `OPENAI_API_KEY` from the nearest `.env` above the project (`LocalEnvFile`); a key set
    elsewhere wins. Without a key both adapters say they are not configured, so the endpoints answer `unavailable` as
    before and nothing is sent.
  - A deployed API with a key refuses to start without `Assistant:Model:ApprovalReference`, which names the decision
    that approves sending family data to OpenAI (start-up problem `assistant_model_not_approved`; open question 52).
  - The adapter logs one fixed code when a call fails (`status_429`, `not_completed`, `refused`, `no_text`,
    `unreadable`, `connection_retried`) and never the request, the context, the reply or OpenAI's error text. A dropped
    connection before any answer is tried once more.
- **Consequences:** In Development with the key, the family context and the conversation now go to OpenAI. Production
  stays blocked on open questions 47 and 52 (consent, data-processing terms, region, retention). The model's words are
  guidance only; nothing in the platform acts on them (rule 7).

## D-120 The Azwah consultant speaks through OpenAI's Realtime API with GPT-Realtime-2.1

*Superseded by D-124 (GPT-Live 1, 2026-11-09). Kept for the record.*

- **Context:** As D-119, for the live voice consultant (D-109).
- **Decision:**
  - `OpenAiVoiceConsultantModel` implements `IVoiceConsultantModel` with a server-side WebSocket to
    `wss://api.openai.com/v1/realtime?model=...`, the key in its `Authorization` header. The phone still talks only to
    the API (protocol version 1 unchanged), so neither the key nor the family context reaches it. The session is
    configured with `AssistantPrompt.VoiceInstructions` (the rules of D-119, spoken: two or three short sentences a
    turn, a one-sentence greeting, the context after a heading), audio only, 24 kHz PCM both ways, near-field noise
    reduction, captions of the person by `gpt-live-transcribe` in the session's language, semantic turn detection that
    creates a reply and lets the person interrupt, voice `marin`, reasoning effort `low`. Once OpenAI confirms the
    settings, the consultant greets the person; an error or a close first fails the start (`model_failed`).
  - The app's 16 kHz microphone audio is raised on the server to the 24 kHz the Realtime API accepts
    (`PcmUpsampler`, straight-line interpolation that keeps its place between frames). The consultant's 24 kHz audio is
    relayed as it comes.
  - Events become the protocol's messages: audio, growing and final captions for both speakers, `turn_end` for each
    finished response, and `interrupted` when the person starts speaking while the last reply may still be playing.
    Over a WebSocket OpenAI cannot know what was played, so the server then cuts that reply
    (`conversation.item.truncate`) at the time since its first audio went out, capped at its length **(engineering
    call: the phone plays as it receives)**. A failed response or an abnormal close ends the session as
    `model_failed`; an error event is logged as a fixed code and the session goes on.
  - Model `gpt-realtime-2.1` **(engineering call)**. Measured on 2026-11-08 with a synthetic spoken question: the
    session opens in 1.7 to 2.5 s, the greeting starts at about 2.6 s, and the reply starts 0.7 to 1.2 s after the
    person stops speaking. GPT-Realtime-2.1 mini answered as fast but rambled, raised the SOS line unprompted, and in
    Arabic called the app "آزواه" instead of عزوة. 2.1 costs US$32 input and US$64 output per million audio tokens
    (mini US$10 and US$20). GPT-Live 1, OpenAI's full-duplex voice model, is not listed for this key, uses a separate
    API that hands reasoning to a backend agent, and would need a new protocol: not chosen.
    `Assistant:Model:VoiceName`, `:Voice`, `:TranscriptionName` and `:VoiceReasoningEffort` change these.
- **Consequences:** A family's voice and captions now reach OpenAI in Development. The 15-minute, idle and
  one-session limits (D-109) bound the cost of a session. Echo is still handled by the phone holding back its
  microphone while a reply plays.

## D-121 Connections to OpenAI race their addresses

- **Context:** On this development network the first request to OpenAI took 44 s from .NET while curl took 0.8 s.
  OpenAI publishes two IPv6 and two IPv4 addresses; IPv6 does not reach it here, and .NET tries addresses one after
  another, waiting for each to time out. That is past the 30-second model limit (D-108).
- **Decision:** both adapters connect through `HappyEyeballs` (RFC 8305): the families alternate, each next attempt
  starts 250 ms after the last or at once when it fails, and the first connection wins; the losers are closed. The
  text adapter's `HttpClient` and the Realtime `ClientWebSocket` each use a `SocketsHttpHandler` with that connect
  callback, pooled for five minutes.
- **Consequences:** A dead route costs about 250 ms instead of a time-out. Measured after the change: a live session
  opens in 1.7 s and the first reply takes 3.2 s.

## D-122 One Azwah agent with tools for the chat and the consultant (no LangGraph)

- **Context:** On 2026-11-09 the owner reported that the assistant and the voice consultant seemed "very sloppy and full of
  bugs", with no intelligence or chat history, and that the consultant's voice kept breaking; they asked for research,
  fixes, "a proper harness around both", production readiness, and agentic flows with LangGraph if required. Live probes
  with synthetic data showed the chat did remember what it was sent, but could do nothing: asked where to get help with
  debts it could only point at All services, and asked what a result meant it said it had no explanation. Design and
  evidence: `superpowers/specs/2026-11-09-assistant-agent-harness-design.md`.
- **Decision:**
  - The agent is defined once in `Application/Assistant`: instructions (`AssistantPrompt`), allow-listed tools
    (`IAssistantTool`, run through `AssistantToolbox`) and the loop (`AssistantAgent`). The chat runs the loop on the
    server; a live call runs the same instructions and tools through GPT-Live's Responses delegation (D-124), with every
    tool executed on the server for the signed-in caller.
  - The loop is an explicit state graph: prepare (instructions, input; the safety check and, for a long chat, the memory
    refresh start alongside) → model (Responses API, strict function tools, parallel calls) ⇄ tools (outputs handed back
    as data) → at most 4 model steps, the last without tools → guard → finish. `IAssistantModel` now takes one step at a
    time (`NextAsync`) and writes memories (`SummarizeAsync`); the adapter replays what a step produced, encrypted
    reasoning included, so `store: false` holds.
  - Tools: `search_support_directory` (the approved directory through `SearchSupportResources`, D-93; at most 5 entries in
    the person's language, information only) and `suggest_app_screen` (`sos`, `services`, `home`, `resilience`, `help`,
    `consultant`, `assistant`). The reply may name an organisation only if the tool returned it; the app shows the
    entries the reply names as cards with their links, and the offered screens as buttons with its own labels. Unknown
    tools, bad arguments and tool faults answer fixed codes to the model; tools run one at a time per request (they
    share its database context).
  - `POST /api/v1/assistant/replies` adds `memory` and per-message `channel` (`chat`, `voice`) to the request and
    `actions`, `sources` and `memory` to the response.
  - **Not LangGraph:** the backend is .NET and LangGraph is Python or JavaScript, so it would add a second service and
    runtime (CONTRIBUTING.md rule 14) and move the context, authorisation, audit and tools across a network boundary. The
    graph has five nodes and is tested like any use case. Revisit if a workflow needs durable state across days.
  - The rules now let the agent be useful within the old limits: a band is reported as recorded and the person is told
    Azwah has not published what bands mean (instead of "I have no explanation"), with help on the areas the answers
    point to **(engineering wording; open question 53)**.
- **Consequences:** Directory searches now start from what a person told the assistant; the directory stays separate
  from scoring and referrals, and its entries are information, not referrals, but D-92's "never linked to answers" is
  now a model choice, not a code path (open question 53). The SupportResources README lists the Assistant as a consumer.

## D-123 Conversation memory across the chat and calls, kept on the phone

- **Context:** The phone sent only the latest 20 messages and nothing summarised the rest; calls kept six caption lines
  in memory and forgot them when they ended; a call knew nothing of the chat.
- **Decision:** The phone keeps the conversation (D-108) plus the consultant's lines (`channel: voice`, saved when a
  call ends) and a rolling memory (at most 2,000 characters) the server writes, all sealed on the phone (mobile
  migration 5). It sends the memory and the latest 40 messages. A request with 30 or more messages also refreshes the
  memory, in parallel with the reply, from the old memory and every message but the latest 20, so nothing that later
  leaves the 40-message window is lost; the app replaces its memory with the one returned. A call starts with the same
  memory and messages (the latest 20 seed GPT-Live; the memory also goes to the voice instructions and the delegated
  backend). The server still stores no conversation; the memory is treated as data, not instructions.
- **Consequences:** A refresh is one more model call on long chats (it adds no wait unless it is slower than the reply;
  the reply waits at most 2 s more for it, then keeps the old memory). Clearing the chat clears the memory.

## D-124 The consultant speaks through GPT-Live 1, delegating to the agent (protocol version 2)

- **Context:** D-120 chose GPT-Realtime-2.1 because `gpt-live-1` was not listed for the key; on 2026-11-09 it is.
  Probed live: full duplex (it listens while it speaks and stops when talked over), audio paced in real time with
  silence as zeros, Responses delegation with our function tools, the conversation seeded through `session.input`, a
  greeting through `session.commentary.append`, US$0.05 a minute plus the delegated model's tokens.
- **Decision:**
  - `OpenAiLiveConsultantModel` replaces the Realtime adapter (and `PcmUpsampler`): a server-side WebSocket to
    `wss://api.openai.com/v1/live/sessions`, so the key and the family context never reach the phone. `session.start`:
    `gpt-live-1`, short voice instructions with the rules it must keep when it speaks on its own (it otherwise explained
    "moderate" by itself), the memory, PCM 24 kHz both ways, voice `marin`, the latest 20 messages as `input`, and
    Responses delegation to `gpt-6.1-terra` (reasoning `low`) with the agent's backend instructions, context, memory and
    tools. Once `session.started` arrives a commentary asks for a one-sentence greeting.
  - Audio is relayed without the silent frames (about 70 % of the stream); `turn_end` marks speech turning to silence;
    transcript deltas become growing lines per speaker, final when the other speaker starts, after a 1.5 s gap, when
    the consultant has been silent about a second, or at the end. A delegated function call runs on the server off the
    audio path; its output goes back with `response.item.create` and, once that response completes,
    `response.create` lets the backend carry on. Neither message takes `delegation_id` (GPT-Live refuses it with
    `unknown_parameter`, found by the live evaluation, D-127). Ending sends `session.close` and waits up to 3 s for
    `session.closed`.
  - OpenAI's own content filter ends a call when someone speaks of hurting themselves (`content_filter`, measured with
    synthetic speech); the server then offers the SOS screen before the call ends, and the app keeps it on screen.
    (2026-11-10: superseded by D-141; the filter ends the model session, and the call carries on in a new one.)
  - The WebSocket connect hedges: a second attempt starts if the first has not connected within 3 s or fails (one
    connect in about ten took 13 s on the development network), and connections race their addresses IPv4 first
    (an IPv6 path could open and then stall a stream for 20 s).
  - A failure on the model's side of a call, its own socket closing included, ends the call as `model_failed`; before,
    a closed model socket was taken for the app leaving and the app was told nothing.
  - **Protocol version 2:** the app's first message is `{"type":"start","memory":…,"messages":[…]}` within 5 s
    (otherwise `error bad_start`, close 1008; validated like a chat request); `ready` carries `"protocol":2` and
    24,000 Hz both ways; the app sends audio continuously (silence while muted, so mute no longer trips the 60-second
    idle limit); the server adds `{"type":"action","action":…}`. Limits and audit as D-109.
- **Consequences:** Measured on 2026-11-09 (D-127, `evaluations/2026-11-09-assistant-and-consultant.md`): the greeting
  starts a median 2.8 s after the call starts and a reply a median 1.5 s after the person stops (5.5 s at the 90th
  percentile, when the backend runs a tool); the open itself depends on the network (1.4 to 13 s here). Open question 52
  covers GPT-Live as well; the filter's spoken refusal before it cuts the call is OpenAI's and is in open question 53.

## D-125 Phone audio: a native voice module with echo cancellation and streaming playback

- **Context:** The voice "kept breaking" because the reply played as half-second WAV files with a gap at each one (the
  next started only on expo-audio's status update, every 500 ms), because Android recorded with `AudioSource.MIC` and
  no echo cancellation (the speaker's sound tripped the loudness barge-in and reached the model, which then cut its own
  reply), and because on iOS expo-audio's stream sets the session to `.record`, which silences playback.
- **Decision:** A local Expo module, `apps/mobile/modules/azwah-voice` (`AzwahVoice`): Android records with
  `VOICE_COMMUNICATION` and the platform's echo canceller, noise suppressor and gain control, and plays through a
  streaming `AudioTrack` for voice communication in `MODE_IN_COMMUNICATION`, routed to the speaker unless a headset is
  connected; iOS uses `AVAudioEngine` with voice processing and an `AVAudioPlayerNode` in a `.playAndRecord`/`.voiceChat`
  session. PCM 16-bit mono at 24 kHz both ways, off the JavaScript thread, with a small jitter buffer. With it the
  microphone stays open while the consultant speaks (full duplex). Without it (Expo Go, Jest) the app uses the
  expo-audio path, mended: silence instead of the echo while a reply plays, a barge-in that must beat the expected echo,
  a 1-second first segment and 50 ms status updates; the screen suggests headphones.
- **Consequences:** The consultant needs a development or store build for full duplex. Verified on the Android emulator
  (API 36) on 2026-11-09: capture is `VOICE_COMMUNICATION` at 24 kHz in `MODE_IN_COMMUNICATION` on the loudspeaker,
  microphone frames arrive continuously (4,458 of 4,462 expected over a 178 s call), playback starts, idles 200 ms after
  the last sound and clears at once, and ending restores `MODE_NORMAL`; the emulator has no echo-canceller effect, so it
  reported `echoCancellation: false` and ran the echo gate. Not yet run on a real phone or with a headset. The iOS side
  cannot be built on this Windows machine and is unverified until it runs on an iPhone (open question 8).

## D-126 Guards and telemetry around both

- **Decision:**
  - **Safety:** the person's last message (and each final spoken line) also goes to `omni-moderation-latest` in
    parallel; any self-harm category, or violence or a threat scored 0.6 or more **(engineering threshold)**, adds the
    `sos` screen whatever the model wrote. A failed check changes nothing; the prompt's SOS rule still applies.
  - **Output guard:** Markdown becomes plain text, spacing is tidied, a reply with no letter or digit counts as empty
    (502) and replies are capped at 4,000 characters.
  - **Retries:** a dropped connection, 429 or 5xx is tried once more after 0.4 to 0.8 s within the 30-second budget.
  - **Metrics** (meter `FamilyResilience.Assistant`, fixed codes only): replies and their duration by outcome, model
    calls by kind and result, tokens by kind and direction, tool calls by tool and outcome, safety flags by channel
    (`chat`, `voice`, `voice_filter`), consultant sessions and their length by ending, and the time to the
    consultant's first sound.
- **Consequences:** The moderation call sends the same text to the same provider as the reply (open question 52).

## D-127 A live evaluation harness

- **Decision:** `backend/FamilyResilience.Evaluation`, a console app in the solution that nothing references and the
  API image never contains (`DependencyRulesTests`). It drives the real adapters with synthetic families, scripted
  multi-turn chats and synthetic speech (OpenAI text-to-speech, cached), checks each reply by fixed rules (memory,
  directory use and cards, no invented numbers, results only for the head, SOS on danger and not on ordinary worries,
  screens, prompt injection, off-topic requests, Arabic, plain text, memory refresh) and by an LLM judge with one rubric
  per turn (`gpt-6.1-sol`), and reports pass rates and latency percentiles in Markdown. It needs the key and costs
  money, so it runs only when asked: `dotnet run --project backend/FamilyResilience.Evaluation -- --suite all --out
  <file>`. Exit code 1 below a pass rate of 85 % (`--min-pass`).
- **Consequences:** On 2026-11-09 it found three faults the unit tests had encoded as correct or could not see (the
  `delegation_id` refusal, the content filter ending calls, a model-socket failure reported as the app leaving); all
  three are fixed. The final run passed 21 of 21 scenarios and 85 of 85 checks, with chat replies a median 3.1 s
  (`evaluations/2026-11-09-assistant-and-consultant.md`). Every prompt or model change should be run through it before
  release; models vary between runs, so a change is judged on repeated runs, not one.

## D-129 The Azwah identity provider: UAE PASS staging as the platform's real sign-in

- **Context:** The owner asked for the real UAE PASS API in the system instead of a mock, using the public UAE PASS
  staging sandbox (2026-11-09). The working UAE PASS staging relying party already existed (D-96), but only inside
  `FamilyResilience.DevIdentity`, a tool that refused to start outside Development, was off by default and needed
  three secrets; the phone showed the fixtures' simulated UAE PASS page instead. The API is a resource server and
  never talks to UAE PASS itself (D-82, D-97), so the platform needs an issuer of its own that can be deployed.
- **Decision:**
  - `backend/FamilyResilience.DevIdentity` is renamed `backend/FamilyResilience.IdentityProvider` (namespace and
    configuration section `IdentityProvider`, Compose service `identity-provider`, variables `IDENTITY_PROVIDER_*`,
    tests in `IntegrationTests/IdentityProvider`; the npm script stays `identity:dev` and the `UserSecretsId` stays
    `familyresilience-dev-identity` so local secrets keep working). It is the platform's OpenID Connect provider, not a
    development tool: the API still never references it and the API image never contains it. D-82 and D-96 keep the
    old name as history.
  - The environment decides the mode. In Development the provider offers the simulated personas (page and
    `persona` choice) and, optionally, UAE PASS staging (`IdentityProvider:UaePass:Enabled`); enabled with an empty
    client ID or secret is a start-up warning and UAE PASS stays off (the hint answers `temporarily_unavailable`), so
    a fresh `.env` still starts. Discovery endpoint URLs follow the request, every page carries the simulated banner
    and discovery and tokens say `azwah_development_only`. Deployed (any other environment name) UAE PASS staging is
    the only sign-in: `/authorize` goes straight to UAE PASS, a `persona` choice is `invalid_request`, the home page
    lists no personas and carries no simulated banner, discovery endpoint URLs come from the issuer, and no
    `azwah_development_only` marker appears anywhere. Start-up refuses without `UaePass:Enabled` plus credentials, an
    `https` `IdentityProvider:Issuer`, `IdentityProvider:SigningKey:Pem`, `https` or app-scheme (`azwah://`) client
    redirect URIs (no `http`, no `AllowExpoGoRedirects`), or a secret for every confidential client. Messages name
    settings, never values. No setting turns personas on outside Development.
  - UAE PASS staging is the default local sign-in: `.env.example` and Compose set
    `IDENTITY_PROVIDER_UAEPASS_ENABLED=true` with the client ID and secret empty. UAE PASS's public sandbox client
    (`sandbox_stage` / `sandbox_stage`, docs.uaepass.ae) is documented and set locally (user secrets or `.env`), never
    committed in a configuration file (rule 6).
  - `IdentityProvider:SigningKey:Pem` accepts an unencrypted RSA private key (PKCS#8 or PKCS#1) of at least 2048 bits;
    the key ID is the first sixteen base64url characters of the SHA-256 of the public key, so it is stable for a given
    key. Without it, in Development, the key stays random per process as before.
  - Signing out ends the UAE PASS session. `GET /end-session` accepts `idp_hint=uae_pass`: after the usual checks it
    keeps the client's post-logout URI and `state` under a random single-use key (hashed, 10 minutes, bounded like
    pending sign-ins) and sends the browser to `IdentityProvider:UaePass:LogoutEndpoint` (default
    `https://stg-id.uaepass.ae/idshub/logout`, same host rule) with `redirect_uri={Issuer}/uaepass/signed-out?r={key}`;
    `/uaepass/signed-out` consumes the key (unknown, expired or reused: the signed-out page, no redirect) and redirects
    to the client. Without the hint, or with UAE PASS off, end-session is unchanged, so the admin dashboard is
    untouched. The mobile app, when its session came from UAE PASS and discovery has an `end_session_endpoint`, opens
    it in the auth browser with `client_id`, `post_logout_redirect_uri` (`azwah://auth/signed-out`, or the Expo Go
    address), a random `state` and `idp_hint=uae_pass` after revoking the refresh token; a cancelled or failed browser
    never blocks the sign-out, and the local session ends regardless.
  - Unchanged: subjects `uaepass-stg|{uuid}` with role `participant`; the access token carries no personal claims and
    the masked profile travels only in ID tokens (D-96); the API stores subjects only (D-97); the code flow with PKCE,
    `state`, `nonce` and RFC 9207 `iss`, single-use codes, refresh rotation with family revocation, RFC 7009
    revocation and security headers; the in-memory grant store (one instance; a restart ends every session, which the
    app treats as expired); and the UAE PASS host rule: every endpoint must be `https` on exactly `stg-id.uaepass.ae`,
    so production `id.uaepass.ae` is refused at start-up until onboarding.
  - **Production UAE PASS is still not onboarded**: no production credentials, registered redirect URIs or official
    button artwork exist (open question 5). The mobile development fixtures keep their labelled simulated UAE PASS
    page, because fixtures mode exists so screens work with no server at all; the real UAE PASS appears in API mode
    against the identity provider.
- **Consequences:** Tests cover the deployed mode (start-up refusals for each missing setting, no personas, no
  development markers, issuer-derived discovery, a configured key with a stable `kid`), the end-to-end acceptance by
  the API of a token from each mode (the deployed-mode token has no development claim), the sign-out chain in the
  provider and the mobile sign-out browser call with its failure paths. A full live staging sign-in still needs a UAE
  PASS staging app account on a phone (OTPs go to a real mobile number and e-mail address), so it remains to be
  completed by the owner; the sign-out chain has likewise only been run against the stubbed UAE PASS, not the real
  logout endpoint. The `Production` environment name is refused while only UAE PASS staging can be configured, so test
  identities cannot become production participants (rule 15); a deployment with staging accounts runs as `Staging`. Deploying the provider (hosting and TLS, key custody and rotation, persistence for more than
  one instance, rate limiting, proxy trust, production onboarding) is open question 55. The numbering leaves a gap
  after D-110, T-25 and open question 47 on purpose: unmerged branches (`feat/score-report`, `feat/openai-models`,
  `feat/mobile-ui-polish`) hold D-111 to D-128, T-26 to T-28 and questions 48 to 54.

## D-130 Demo buttons: fill a questionnaire for a low, medium or high result

- **Context:** The owner asked for three buttons for demonstrations (2026-11-09): pressing one fills in a questionnaire
  so the family resilience result comes out low, medium or high, as chosen. The app may not hold scoring logic (rule
  1): published versions carry no option values, directions or bands, so only the server can tell which answers give
  which result.
- **Decision:**
  - The server chooses the answers. `DemoAnswerPlan` (Domain/Scoring) takes the scope's items and the approved
    `summed_item_values` parameters and picks one answer per item: **low** aims at the lowest tier band, **high** at
    the highest, **medium** at the band holding the middle of the possible totals; the total aimed at is the middle of
    that band, rounded down (with no bands: the lowest, middle or highest possible total). The points above each item's
    lowest value are spread evenly along the items, reverse items are answered on the mirrored side, unscored items take
    their first substantive option and a non-substantive answer (99) is never chosen. For the draft household
    assessment proposal (D-99) that is 58 (vulnerable), 98 (transitional) and 137 (flourishing).
  - `GET /api/v1/sessions/{sessionId}/demo-answers?level=low|medium|high` (policy `SessionParticipant`) returns the
    answers in item order. It saves nothing and returns no score or band: the app chooses the answers as if the person
    had, they are sent through `PUT …/responses`, and submission scores them with the approved rules like any others.
    `DemoAnswerPlanner` uses only the single approved scoring rule set for the session's scope, as scoring does (409
    `scoring.policy_not_approved` without one), and checks its own plan with the same strategy before returning it. An
    in-progress session only (409 `sessions.not_in_progress`); another family's session is 404 as everywhere; each plan
    is audited as `sessions.demo_answers.planned`.
  - Off by default. `DemoTools:Enabled` switches it on, and only `appsettings.Development.json` sets it; otherwise the
    endpoint answers 404 `demo_tools.disabled`. A deployed API refuses to start with it on (`demo_tools_enabled` in
    the start-up guard), because the answers are made up and must never reach real data (rule 15).
  - The app shows the three buttons on the question screen, in a card titled "Demo: fill in the answers" that says the
    answers are made up, in development fixtures mode or when `EXPO_PUBLIC_DEMO_TOOLS=true`. Pressing one fills every
    answer, saves it and opens the review, where the person submits as usual; a refusal shows a translated notice and
    changes nothing. In fixtures mode `simulateDemoAnswers` mirrors the server's rule over the proposal, as
    `simulateScoring` mirrors scoring.
- **Consequences:** A demonstration still needs an approved scoring rule set for the questionnaire on its server (none
  is approved for real content; open question 1), or fixtures mode. Nothing scores on the phone, and an answer filled
  this way is indistinguishable from a typed one except through its audit event, so the switch must stay off wherever
  real families answer; see T-30. Tests: the plan's band and total for each level, a property test over random scopes
  and bandings scored by the real strategy, the endpoint end to end (each level saved, submitted and scored in its
  band; 400, 401, 403, 404 both ways, 409 both ways), the guard, and the app's buttons, HTTP call and fixture planner.
- **Merged with the demonstration database (2026-11-10):** the demonstration database (D-140) runs the API in
  Development, so `DemoTools:Enabled` is on there and its approved household assessment rule set gives the buttons
  their bands; a "low" result there also raises a score alert through the outbox when score alerts are on (D-136),
  as synthetic as the score. Saving the answers is refused once the assignment has expired (D-139), like any answer.
- **Superseded in part by D-143 (2026-11-11):** one tap now answers, submits and scores on the server, and the phone
  no longer saves the answers or opens the review; the plan endpoint stays for measurements.

## D-131 A demo household button in development builds

- **Context:** The owner asked for a button that fills in a family household for demonstrations, fully working: when
  pressed it must create a household in the application, not just fill in the form (2026-11-09). Setting up a
  household by hand means typing every person's details one at a time, which is slow in front of an audience.
- **Decision:**
  - The household start screen (`HouseholdStart`) of a **development build** (`__DEV__`) offers a secondary button,
    "Auto-fill a demo family", beside Start, with a notice that says it is for demonstrations only and what it fills
    in. A release build never shows it, in any mode, so a release build cannot create synthetic participant data
    (rule 15).
  - One press sends one ordinary `POST /api/v1/households` with a made-up family of six, the caller marked `isSelf`:
    the caller (42, employed, head of household and main earner), a spouse (39, self-employed), three children
    (19 and a student, 15, 10) and the head's parent (70, retired). Ages are fixed and birth years follow from the
    phone's date (`src/features/household/demoHousehold.ts`). There are no names or identity numbers, as in every
    household. In fixtures mode it goes to the fixture API; in API mode to the real API, under the signed-in
    account.
  - It reuses the creation path of the "about you" form (`rosterRequests.createHousehold`, now taking the other
    members): presses while one is running are ignored, a retry after a lost answer opens the household the lost
    request created instead of asking for a second, and an account that already has one is led to it. The roster
    then opens with a "Demo household created" notice; confirming it stays a separate step.
  - Nothing on the server changes: it validates the roster and refuses a second household per account as for any
    request, and assigns questionnaires from approved rules only when the roster is confirmed. The family is not
    eligibility or research content.
- **Consequences:** Each account can still have only one household, so each demonstration needs an account without
  one: in fixtures mode the New participant persona after the app reloads (the fixture world is in memory); in API mode
  a fresh account, or a fresh development database (`npm run db:reset`, `db:migrate`, `db:seed`) before signing in
  again as `participant_new`. A demo household created in API mode looks like any other household to the server and
  to analytics; it is labelled only on the phone, so a database that holds one is not fit for real data. Tests cover
  the family (ages across year ends and a leap day, one adult head and main earner, a roster that confirms as it is),
  the button (one request with the whole family, repeated presses, a lost answer, an account with a household) and its
  absence from release builds. D-130 is held by the unmerged `feat/demo-score-buttons`, whose demo card is switched on
  by `EXPO_PUBLIC_DEMO_TOOLS` in API mode; this button needs no switch because it calls no demo endpoint. When both
  are merged, one switch for every demo control may be simpler.
- **Merged with D-130, D-139 and D-140 (2026-11-10):** fixture data is now kept on the phone across reloads (D-139), so
  in fixtures mode the New participant persona starts again only after Settings → Development tools → **Reset
  demonstration data**, not after a reload. In API mode the demonstration database (D-140) has `participant_new`, an
  account with no household: reset it with `npm run demo:reset` before each run. The family's four adults (42, 39, 19
  and 70) are certainly 18 or over on any day, as the adults-only rule now requires (D-139), so the server treats
  them as adults. The two switches stay separate: this button shows in every development build, the score buttons
  only in fixtures mode or with `EXPO_PUBLIC_DEMO_TOOLS=true`, and only the score buttons need the server's
  `DemoTools:Enabled`.

## D-132 The family's resilience over time: a chart of recorded totals for the head of household

- **Context:** On 2026-11-09 the owner asked for a chart that tracks the family's resilience each time it takes the
  survey, placed wherever it fits in the app. The family's score is the household assessment's total and band, shown
  to the head of household only (D-103). No scoring rule set is approved yet, so no real family has a score.
- **Decision:**
  - `GET /api/v1/me/resilience-trend` (participants, policy `Participant`; every read audited as
    `scoring.resilience_trend.read`, resource type `resilience_trend`) reads the submitted sessions of a
    household-assessment instrument (kind `HouseholdAssessment`) answered by the members the caller may act for, the
    40 newest at most, and returns them oldest first. Each point is a session's latest result with a total: session,
    wave code, submission time, total and tier exactly as recorded, and a segment number. Family-voices modules are
    never included: the two instruments are never combined.
  - The scale (lowest and highest total, and the tier bands in order) comes from the approved rule set that recorded
    the latest total, read from its definition and the version's content the way scoring reads them
    (`ScoreScaleReader`, `ScoringParameters.TotalScale`); nothing is assumed. A total recorded under a rule set with
    another range or other bands is left out rather than drawn against bands it was not scored with; pending and
    incomplete check-ins are not drawn either, and `notShownCount` counts them all. Points join only while the same
    rule set scored them one after another: a new rule set starts a new segment, because totals from different rule
    sets may not be comparable **(engineering call)**.
  - Only the head of household gets the scale and the points. Anyone else gets `scoreVisibility:
    head_of_household_only` and nothing more, as with results and history (D-103).
  - The mobile app shows it on "Your family resilience" (`/resilience`), after the questionnaires still to answer and
    above the past questionnaires it summarises **(engineering call: the owner left the place open; answering the
    survey stays the screen's first action, D-105)**. "Your resilience over time" is a line chart in pine with one
    evenly spaced point per round (the owner asked for each time the family takes the survey, so the axis counts
    rounds, not calendar time); the band regions are shaded alternately and named in the reader's language (the D-103
    names; any other code as recorded); the axis is labelled at the ends of the scale and where each band begins. The
    latest round is shown above the chart with its total, band and date. Each point is a 48-point button: tapping it
    (or choosing it with a screen reader) shows that round instead. "Show every round" lists them all, newest first.
    Captions say when the line starts (one round), that the scoring rules changed (more than one segment), and how
    many rounds are not on the chart. The app computes no score, band or change: it places the server's values on the
    server's scale. The head-only and empty states are translated notices.
  - It is drawn with `react-native-svg` 15.15.4, installed with `npx expo install` (Expo Go includes it; a
    development build must be rebuilt). SVG has no layout direction, so positions are measured from the start edge and
    mirrored right to left (`trendGeometry.ts`); labels and touch targets are views placed with `start` and `end`.
  - The development fixtures give the household lead an older scored round (`dev-wave-0`, scored by the simulated
    proposal, total 69, `vulnerable`) besides the existing one, so fixture mode draws a line.
- **Consequences:** No real family sees a chart until the household assessment's version and
  `household_assessment_total` v1 are approved (D-99, D-103); until then the screen says there is no score to chart
  yet. Whether a change between rounds should be put into words for families (for example "up 12 points"), and how,
  is not decided, so the app shows none (extends open question 3). Comparing totals across instrument versions is a
  research decision; the segments keep them apart until one is made. Numbered D-132 because unmerged branches
  (`feat/demo-score-buttons`, `feat/demo-household`) hold D-130 and D-131.

## D-135 Safety alerts: a durable record of a possible crisis, with no words in it

- **Context:** On 2026-11-09 the owner asked that when the assistant, the voice consultant "or anything in the app that
  can take user input" sees a person with a very low resilience score or in a critical mental state ("suicidal
  thoughts or any bad intentions"), an alert reaches the admin dashboard. Design:
  `superpowers/specs/2026-11-09-safety-alerts-design.md`.
- **Decision:**
  - A new backend module, `Safeguarding`, owns one table, `safety_alerts`. A row holds fixed codes only, never what the
    person said: the subject (platform user, household and the caller's own member, taken from the server and never
    from the client), the source (`assistant_chat`, `consultant_voice`, `support_search`, `household_assessment`), the
    concern (`suicide_or_self_harm`, `harm_to_others`, `abuse_or_danger`, `low_resilience_score`), the severity
    (derived: `critical` for the first three, `high` for the score), the detector, a signal code, the language, a
    count, times, a status (`open`, `acknowledged`, `resolved`) and the resolution (`family_contacted`,
    `referred_to_service`, `emergency_services_contacted`, `no_action_needed`, `false_alarm`, `unable_to_contact`). A
    database trigger forbids changing the subject, source, concern, severity and first detection, and forbids deletes
    except during household erasure, which deletes a household's alerts first.
  - Other modules call it only through `ISafetySignalRecorder`. **Dedupe:** at most one unresolved alert per user,
    concern and source (a repeat adds to its count and moves its last time and code, and **re-opens an acknowledged alert**: status back to `open`, the acknowledgement cleared, who acknowledged it kept in the audit trail, so a repeat pages staff again and acknowledging cannot silence a returning person), enforced by a partial unique
    index, and one score alert per scored session, so an outbox retry or a staff re-score raises nothing new. A lost
    race, or an `xmin` collision between two repeats, is retried once: the recorder re-reads the unresolved alert and
    folds into it. Nothing is dropped.
  - **Recording never fails the person's request.** The recorder saves each attempt in its own dependency-injection
    scope and resolves the caller (`ICallerAccess`) in that scope, so a failed save never spoils the request. It has its
    own 5-second budget, built on `TimeProvider`. A time-out or fault is logged as the fixed code
    `safety_alert_failed`, counted as `azwah.safeguarding.recording_failures` (tag `source`) and returned as `Failed`;
    the request goes on. A signed-in participant with no platform user record is `Skipped` and counted as `azwah.safeguarding.recording_skipped` (tag `source`). The failure log names only the exception's type, or the refused error's code, never a message. The hooks pass
    `CancellationToken.None`, so a person who sends a crisis message and then closes the app, or hangs up the call,
    still raises an alert. New alerts are counted as `azwah.safeguarding.alerts_raised` (tags `concern`, `severity`, `source`) and
    audited `safeguarding.alert.raised`; a repeat is counted as `azwah.safeguarding.alerts_repeated` (same tags, so a repeat pages as a new alert does) and audited `safeguarding.alert.repeated`. The counters are in the meter `FamilyResilience.Safeguarding`.
  - **The System actor (2026-11-10).** `safeguarding.alert.raised` and `…repeated` are audited as the System actor, not the participant: the detector acted, and audit events outlive erasure with their actor subjects (retention policy §4), so a trail of crisis signals under the person's own subject would outlive the erasure of their alerts. Staff actions (`safeguarding.alerts.read`, `…acknowledged`, `…resolved`) name the staff member.
  - **Timing.** The chat and the support search start the recording beside the model or search work and wait for it before answering. Never call the recorder inside a transaction that locks or creates the subject's rows: it saves in its own scope and would wait on that lock until its budget ran out.
  - **Erasure (2026-11-10).** Household erasure removes alerts by household, by the household's sessions, or by the
    accounts being erased. It locks those accounts first and never keeps an account for an alert's sake.
  - Admin API (policy `SystemAdministrator`; permissions `safety_alerts.read` and `safety_alerts.manage`): `GET
    /api/v1/admin/safety-alerts` (paged, filtered by the query parameters `status` and `category`; audited `safeguarding.alerts.read` on each list fetch, resource type `safety_alerts`), `GET
    /api/v1/admin/safety-alerts/summary` (open, acknowledged and open-critical counts and the newest detection time;
    counts only, polled in the background, so not audited), `POST …/{alertId}/acknowledge` and `POST …/{alertId}/resolve`
    (audited `safeguarding.alert.acknowledged` and `…resolved`, resource type `safety_alert`). Reads use the `StaffReads` rate limit, writes
    `StaffWrites`.
  - Prometheus rules `SafetyAlertRaised` (critical), `LowResilienceScoreAlertRaised` (warning),
    `SafetyAlertRepeated` (critical, on the repeat counter), `SafetyAlertRecordingFailing` (warning) and `SafetyAlertRecordingSkipped` (warning) in `infra/monitoring/alert-rules.yml`, each proven by promtool (D-90). The annotations do not claim the person always saw the crisis card (a line still being spoken at hang-up, a phone timeout or a closed app can prevent it) and say plainly that a failed alert may be lost: notices are not logged, so an operator correlates the failure time with `assistant.reply.generated` audit events.
- **Consequences:** This amends D-108's "the server stores no conversation": it still stores no text, but it now
  stores that a person triggered a concern, which is itself sensitive (T-33). Alerts join household erasure and the
  retention policy (period not approved; open question 59). The branch `feat/safety-alerts` is based on
  `feat/openai-models` (D-119 to D-127) with main merged in, so merging it brings `feat/openai-models` along. The
  numbering leaves a gap after D-129, T-29 and open question 55 on purpose: unmerged branches hold D-130 to D-134, T-30
  to T-32 and questions 56 and 57.

## D-136 What raises an alert: a phrase list, the moderation verdict, the provider filter and the lowest score band

- **Decision:**
  - **Phrase screen.** `crisis-signals.v1.json` (version `crisis-signals.v1`, status
    `engineering_starting_list_not_clinically_reviewed`) holds 9 rules in 3 concerns, in English and in Modern
    Standard and Gulf Arabic. It runs on the server on every piece of participant free text, before any model and
    without one (2026-11-10: in the assistant chat it runs before the model-configured check, so a 503 also carries the
    notice). Text is normalised (lower case; Arabic diacritics, tatweel, zero-width joiners and Quranic marks
    removed; alef, taa marbuta, alef maqsura and hamza seats folded; Arabic-Indic digits made Western; apostrophes
    dropped) and a phrase matches only as whole tokens, with one clitic allowed before its first Arabic word (2026-11-10: و or ف "and", ب or س "will" or a preposition, or و/ف then ب/س), only when at least three letters remain after it. The list also holds inflections ("killing myself", "hurting myself", "he hit me") and more family subjects, and an `except` list cancels everyday uses ("hits me up"). When
    several rules match the priority is suicide and self-harm, then abuse and danger, then harm to others.
  - **The list is active although unreviewed (engineering call).** A false alarm costs a staff member a minute; a miss
    can cost far more. Everyday idioms were removed on purpose (bare "hits me", "beats me", "going to kill me",
    بيقتلني, يبي يقتلني, a bare "overdose", اذبحه, يعذبني) and abuse phrases are led by a subject ("husband hits me",
    "my dad hits me"). Some ambiguous phrases stay and are pinned by tests as accepted matches for human review:
    "want to die", "kill them", تعبت من الحياة. Review of the list is open question 58.
  - **Where it runs (2026-11-10).** On the participant's own words only. The assistant's own directory tool
    (`SearchSupportResources.SearchAsync`) is not screened, because its query is the model's words; only the
    participant's words raise alerts under their name. Directory search is screened only for callers with the
    participant role, never staff. A participant's search for "suicide prevention helpline" raises a critical alert by
    design: help-seeking on that topic is worth a human look, and staff can resolve it as `no_action_needed` (open
    question 58 reviews this).
  - **Moderation verdict.** D-126's `Danger` verdict is split into danger to self (any self-harm category) and danger of
    violence (violence or a threat at the 0.6 engineering threshold; the verdict `DangerOfViolence`, renamed from `DangerToOthers`), with self-harm taking priority. Violence records `abuse_or_danger`, not `harm_to_others`, because moderation cannot tell who is at risk (the phrase list still raises `harm_to_others` for phrases like "kill him"). It records an
    alert when the phrase screen did not; when it flags a different concern from the phrase match, that concern is
    recorded too, with detector `ai_moderation`.
  - **Provider filter.** When OpenAI's content filter ends or blocks a call about self-harm (D-124, open question 53)
    the consultant records `suicide_or_self_harm` with detector `provider_filter`. In the call, the notice and SOS are
    sent first; the recording runs off the audio path, and settling awaits it after the model session closes. A line
    still being spoken at hang-up, and so never finalised, is screened by the phrase list at call end; moderation does
    not see it, because it is cancelled with the call. (2026-11-10: the call now carries on after the stop, and a stop
    before the person spoke records nothing, D-141.)
  - **Score band.** An outbox handler for `scoring.score_result_recorded.v1` records `low_resilience_score` when the
    result's rule set and tier are listed in `Safeguarding:ScoreAlerts:Tiers`. The key is the scoring rule set's code
    (`household_assessment_total`), not the scoring policy code (`summed_item_values`). The default is
    `household_assessment_total` → `["vulnerable"]`, the lowest band (39–78) the owner approved in D-103. Only results
    of approved rule sets exist, so nothing fires until the rule set is approved (D-99).
    `Safeguarding:ScoreAlerts:Enabled=false` turns the trigger off (default true); tiers are overridden per index
    (`Safeguarding__ScoreAlerts__Tiers__household_assessment_total__0=<tier>`). Invalid codes or settings fail start-up
    (`ScoreAlertStartupGuard`). Pending score messages from before the handler existed are delivered once; none raises
    anything while no household-assessment rule set is approved.
  - No AI scores, bands or decides anything about a family (rule 7): moderation only flags text for a human.
- **Consequences:** A person who writes in another language, in code or in a way the list does not hold gets no alert
  from the phrase screen; moderation covers some of that, only where the key is set (T-33). The list can change only
  by a new version of the file and a release.

## D-137 The Safety alerts page, the navigation badge and the new-alert toast

- **Decision:**
  - A page `/safety-alerts` in the admin dashboard (permission `safety_alerts.read`; acknowledging and resolving need
    `safety_alerts.manage`). **System administrators only:** programme administrators never see scores (D-62), and a
    score alert reveals one, so they see no alerts at all. Who belongs on a safeguarding rota is open question 59.
  - The list is filtered by status (default "needs attention": open and acknowledged; also resolved, all) and by
    concern, and is a 5-column table with visible actions (not hover-only); its rows show severity, concern, source, household reference, the member's relationship, first and last seen, count and status. An unknown source or detector value is shown as recorded. Acknowledge is one click; Resolve opens a dialog with the six outcome codes. Codes are named in
    English and Arabic and an unknown code is shown as recorded; relationship codes (`head`, `spouse` and the others) are
    labelled the same way. A guidance panel says the alert holds no
    conversation, to follow the organisation's safeguarding procedure and to call 999 for immediate danger, and that the
    platform holds no contact details (D-97).
  - A navigation item "Safety alerts" with a badge counting open and acknowledged alerts, red when any open alert is
    critical. The badge's label says "{count} safety alerts need attention" (plural forms in English and Arabic). The
    shell polls the summary in the background every 30 seconds, counts only, so the tab title shows a count too, and it
    shows a toast when the newest unresolved detection moves forward; a critical toast stays until dismissed. When the
    summary cannot be refreshed the badge is marked stale, and an expired session shows a toast.
  - **The list does not poll.** It refetches on mount, on focus, after an action and when the watcher sees a change,
    so `safeguarding.alerts.read` is not written every 30 seconds for each open tab (T-33). Acknowledge and Resolve
    show only for staff with `safety_alerts.manage` (`canManage`).
  - The BFF allow-lists four routes, each with a Zod schema and a mock-mode equivalent (the contract test covers them).
- **Consequences:** **Polling, not push, is an engineering call:** it needs no new server channel, and a delay of up
  to 30 seconds is acceptable beside the Prometheus rule, which pages an operator. A staff member who is not signed
  in sees nothing; the rule and a rota (open question 59) are what reach them.

## D-138 Crisis support on the phone, and the `safety` message

- **Decision:**
  - The server sends a notice whenever a detector fires, **even if recording the alert failed**: the notice must never
    depend on the database. In the assistant reply it is the field `safetyNotice: "crisis_support"` (with SOS as the
    first action); on a 502 or 503 the ProblemDetails carries the same value as an extension when the phrase screen
    matched; the support search response gains `safetyNotice`; on a call the relay sends
    `{"type":"safety","notice":"crisis_support"}` once. The consultant protocol stays version 2: the app ignores
    unknown message types.
  - **The notice survives database faults (2026-11-10).** In the chat and the support search the phrase screen runs
    and the notice is returned even if the database fails, and on any failure status the ProblemDetails carries it as
    the extension `safetyNotice` (D-135's recorder never throws into the request).
  - The app shows a crisis support card (design system): "You don't have to face this alone", a line to call 999 if
    they may hurt themselves or someone else or are in danger, "Dial 999" (opens the phone's dialler with 999 through
    `openDialler`), "Emergency numbers" (`/emergency`), "We've let the Azwah support team know." and Dismiss. It
    appears in the conversation itself, after a reply or an error carrying the notice (until dismissed or the chat is
    cleared; never stored), on the call screen (the call goes on; the card's actions end the call first) and at the top
    of All services, which also shows it when a failed search carries the notice. The phone parses the notice
    leniently (any string shows help), and the assistant reply has a 45-second timeout on the phone. The phone shows
    what the server says and screens nothing itself.
  - The card's note "We've let the Azwah support team know." shows whenever the notice arrives, even if recording
    failed. The notice never depends on the database; failures alert operators (`SafetyAlertRecordingFailing`, `SafetyAlertRecordingSkipped`). A person whose phone timed out after 45 seconds, who hung up while a line was still being spoken, or who closed the app may never see the card even though an alert exists.
  - The SOS-list search stays on the phone and offline (D-105) and is never sent, so it raises no alert.
- **Consequences:** The Arabic copy is engineering wording awaiting native and clinical review (open question 58). The
  note "we've let the team know" is shown even when recording failed; the metric and rule of D-135 are what tell
  operators. Whether and how participants are told that alerts exist is open question 59.

## D-139 Hardening pass before demonstration: guards, expiry, suppression by people, export fields, dependencies

- **Context:** On 2026-11-10 the owner asked for a full audit of the platform against the survey manual and codebook
  and for the defects to be fixed before the platform is shown to judges and researchers. Six read-only audits
  (survey fidelity, calculations, backend security, dashboard, phone app, demonstration readiness) found no critical
  security or content defect: the codebook, the manual and the content files agree with 0 differences, a 99 answer is
  never scored, and nothing unapproved runs outside the phone's fixtures. They did find the defects below.
- **Decision:**
  - **Adults only means certainly 18.** Only the birth month is stored, and ages were computed as if the birthday were
    the first of that month, so a 17-year-old passed the adults-only guard for up to a month. The guard, consent, the
    manager at enrolment and account linking now use `YearMonth.IsCertainlyAtLeast`: an adult from the first day of
    the month after the birth month, 18 years on. Rule-set thresholds (25 to 55, 60 and over) keep the documented
    first-of-month convention, which is the research owner's to change (open question 62).
  - **Assignments expire.** `AssessmentAssignment.Expire` had no caller: answers and submissions were accepted after
    the wave closed, then scored and exported under it, and monitoring's "expired" count was always 0. Saving and
    submitting after the expiry now answer 409 `assignments.assignment_expired`, and the maintenance service marks
    overdue assignments Expired, abandoning a session still in progress first.
  - **Removing a member closes their open work.** Deactivation left a session in progress that nobody could finish or
    abandon. It now abandons the member's sessions in progress and cancels their open assignments in the same
    transaction (`IMemberAssessmentCloser`).
  - **Suppression counts people.** D-58 hides cells covering fewer than 10 people, but the queries counted assignments
    and score results, so one person with several assignments or results across waves could fill a cell. Demographic
    cells and score distributions are now suppressed on distinct members; displayed counts keep their meaning and the
    response contract is unchanged. `activeMembers` leaves out withdrawn households.
  - **Exports can tell a 99 from an answer.** `assessment_sessions` gains `non_substantive_count`; `score_results`
    gains `incomplete_reason` and `excluded_count`, so a prorated total is distinguishable from a full one, as the
    manual's rule on code 99 needs. `total_score` is written without trailing zeros. No item-level answer is exported
    (open question 12).
  - **Inputs.** A number in place of an enum name in a JSON body is refused, and a bad enum in a query string gets a
    fixed message that does not repeat the value. Two results of one session computed at the same instant no longer
    both count as the latest.
  - **Dependencies.** The audit gate failed on `main`: Next.js 16.4.0 had two critical advisories (unauthenticated
    remote code execution) and Zod one high. The dashboard moves to Next.js 16.4.6 and both apps to Zod 4.6.7, with
    patched transitive packages; the stale `braces` and `node-forge` allowlist entries are removed. One advisory is
    allowlisted until 2027-01-17: `decode-uri-component` under `expo-router` 57 (the fixed release is ESM-only and
    outside the router's range; it decodes the app's own deep links on the person's own phone).
  - **Dashboard mock data is one population.** The overview's figures were hand-written and contradicted the lists
    generated beside them. Every mock figure is now counted from one seeded synthetic population with the API's
    definitions and suppression rule, the scored instrument is the household assessment with D-103's bands derived
    from each total, and a test fails when the mock drifts from `ExportCatalog`. Charts are laid out left to right
    inside a right-to-left page, because SVG text anchors swap under `dir="rtl"`.
  - **Phone fixtures survive a reload.** A language change reloads the app and used to wipe the fixture world. It is
    now kept on the phone, with a fixtures-only "Reset demonstration data" action. In fixtures mode the welcome screen
    no longer shows the server card; a draft version's description is hidden; an Arabic reader is told when some
    answer wording has no approved Arabic yet and is shown in English; Back from a finished flow goes Home.
- **Consequences:** No schema change and no change to `backend/openapi/v1.json`. Clients see the new 409 on answering
  after expiry (the phone treats it as a closed questionnaire and stops retrying), the three export columns and the
  stricter enum handling. The audit fixes also moved the dashboard's `shadcn` build tool from 4.21.4 to 4.23.0. What
  the audits found and this pass did not decide is in open questions 60 to 68 and the extension of 29. Tests passing
  a build are not a scientific validation of the instruments: nothing here is approved by a research owner.

## D-140 A local demonstration database, labelled and separate from every real one

- **Context:** A stack seeded with `db seed` cannot be demonstrated: nothing is approved, no wave is open, and no
  code path opens a wave or approves a consent policy, so nobody can consent, be assigned, answer or be scored, and
  every dashboard chart is empty. The owner had run earlier demonstrations from a hand-made local database,
  `azwah_demo`, in which the household assessment and its proposed scoring were approved locally with nothing real in
  it (2026-11-06). The hardening pass (D-139) was asked for a reliable, repeatable demonstration with a reset.
- **Decision:**
  - `db demo` builds a demonstration database and `db demo --reset` rebuilds it (`npm run demo:db`, `demo:reset`,
    `demo:api`). The target is always derived from the configured connection as `<name>_demo`; the ordinary database
    is never written to. The command and the `Database:UseDemonstrationDatabase` setting are refused outside
    Development; a deployed API refuses to start with the setting or with a connection string that names a `_demo`
    database; and the seeder refuses any database whose name does not end in `_demo`. A reset drops and recreates
    that database, because audit events are append-only and approved content cannot be deleted in place. Before it
    drops or migrates an existing `_demo` database, the command checks that the database is empty or carries the
    demonstration's own marker; one made any other way, such as the hand-made `azwah_demo` of earlier
    demonstrations, is left untouched unless the operator passes `--replace-foreign-database`.
  - In that database only, the household assessment version 1, its scoring rule set `household_assessment_total` v1,
    a demonstration eligibility rule set (`demo_household_assessment_head`: the head of household answers it) and one
    placeholder consent policy per purpose are approved, each with the approver `azwah-demonstration-seed` and the
    source "LOCAL DEMONSTRATION ONLY (D-140): not approved by a research owner".
  - The Abu Dhabi Family Survey stays a draft and its eligibility rules stay proposed there as everywhere: its Arabic
    is incomplete and it has no scoring proposal, and the demonstration does not work around either.
  - It holds 60 synthetic households (205 members, no names or contact details), a closed wave and an open one, and
    sessions answered through the domain and scored by the platform's own engine, so each wave shows all three bands
    above the suppression minimum. The Development identity provider's personas are linked: the household lead can
    answer the open wave's assessment live, the new participant can enrol live.
  - No referral rule set, partner organisation or support resource is seeded: none is sourced or approved.
- **Consequences:** The real platform is unchanged: it still approves nothing until a named approver acts (D-99), and
  rule 15 (no synthetic participant data in production) is enforced by the environment and name guards (T-34). The
  head-of-household rule for the household assessment is a demonstration rule, not a proposal; who answers that
  instrument is open question 66. The owner should confirm at merge that approving the real draft content in a
  labelled local database is still what they want; the alternative is a synthetic sample instrument. Since the safety
  alerts were merged (D-136), every seeded score in the vulnerable band also raises a score alert once the API's outbox
  dispatcher runs against the database; the build records none itself, and the alerts are as synthetic as the scores.
  Presenting it: [`demo-guide.md`](demo-guide.md).

## D-141 A call about self-harm carries on: support after OpenAI's filter, in a new model session

- **Context:** On 2026-11-10 the owner said: "the call shouldnt get cut just cause the user said something about self
  harm, rather it should act like a consultant and empath with the user and calm him/her down" (part of open question
  53). Until then (D-124, D-135 to D-138) OpenAI's filter ended the GPT-Live session and the relay ended the call after
  SOS. It cannot be switched off. Measured on 2026-11-10 with synthetic speech: the person's words are transcribed as
  they speak; GPT-Live starts its own reply (in every measured case a supportive one: "I'm really sorry you're feeling
  this way.", not a refusal); 1.8 to 5.6 s after the person stops, an `error` with code `content_filter` ("The
  generation was stopped due to moderation") and usually, within 25 ms, `session.closed` with reason `content`, then the
  socket drops (once the session stayed open, as OpenAI's guide says some moderation does). A second defect was reproduced: the phone sends the call's lines as the next call's history, and a line that
  tripped the filter, seeded as `input`, has every later call closed for content 0.8 s after `session.started`, before
  anything is said. A crisis memory in the instructions did not trip it.
- **Decision:**
  - **The call outlives the session** (`ContinuingConsultantSession`, in the Application layer, so the relay and the
    protocol are unchanged). When a session is stopped for content after the person spoke in it: the provider-filter
    flag (recorded as before: `suicide_or_self_harm`, `provider_filter`, at most once a concern a call; the notice once
    a call) and SOS go first, then `interrupted` (the phone drops what is still buffered) and an empty final consultant
    line, which the app neither shows nor saves, so the cut reply is not kept in the chat. A new GPT-Live session opens
    at once that does not greet, starts from the call's screened earlier conversation and memory and a fixed note in
    Azwah's own words (`AssistantPrompt.CrisisResumeNote`, measured: accepted, and the session stays quiet until spoken
    to), never from anything the person said, and is asked to say the supportive reply word for word: the words as
    `session.instructions.append`, then `session.commentary.append` with `AssistantPrompt.SpeakNow`. Measured: an
    instruction alone was said as given but, in 2 of 10 stops, held until the person spoke again, which left silence
    after the stop; with the commentary after it, speech started 1.3 to 3.5 s later (median 1.9 s), word for word, in
    14 of 14 sessions (commentary alone took 2.2 to 2.3 s and may paraphrase). Any stop the filter signals is handled
    so, also an error that leaves the session open. The call then carries on, full duplex.
  - **The reply** is written while the session opens by the crisis model (`IConsultantCrisisReply`,
    `OpenAiCrisisReply` on the shared Responses transport, `Assistant:Model:CrisisName` default `gpt-5.4-mini`,
    `Assistant:Model:CrisisReasoningEffort` default `none`; measured 1.8 to 2.9 s, against 2.8 to 7 s for
    `gpt-6.1-terra`) from the person's latest line only, under `AssistantPrompt.CrisisReplyInstructions`: acknowledge,
    thank them, stay with them, slow down, ask gently whether they are safe now, encourage someone they trust nearby,
    the SOS button or 999; never diagnose, claim to be a professional, promise confidentiality or outcomes, or discuss
    methods; never end the conversation; two to four sentences. If it is not written within 5 s, is refused or is
    unusable, the session says fixed words with the same content (`CrisisFallbackWords`, English and Arabic).
  - **Differs from the recommended design:** the reply is spoken by the new GPT-Live session, not by OpenAI
    text-to-speech. Same model and voice as the call (`marin`), the person can talk over it and is heard at once, the new
    session knows what it said, there is no audio to time or mute, and OpenAI's speech models for `/v1/audio/speech`
    (`gpt-4o-mini-tts`, `tts-1`) shut down on 2027-01-06. The cost is time: text-to-speech gave first audio 1.5 to 2.3 s
    after a request, while GPT-Live must first open. Because the session can speak, the fallback is fixed spoken words,
    not silence.
  - **While no session listens** (from the stop until the reply is ready and the new session open) the microphone is
    dropped, not held: GPT-Live documents no replay of buffered speech, and the gap is short. SOS and the crisis card are
    on screen throughout.
  - **Cap:** five carry-ons a call (`AssistantLimits.ConsultantResumeLimit`). Only the person's own words cause one, so
    the cap stops a loop, not a conversation; the 15-minute limit still applies. The next stop opens a last session that
    says `CrisisClosingWords` (it must end the call, they are not alone, SOS or 999 now, go to someone they trust), with
    the microphone unheard, and the call ends normally (`model_ended`) once the words have played, at most 30 s.
  - **A stop before the person spoke in a session** was caused by what the session was started with: nothing is
    recorded or shown (nothing new was said) and it is reopened once without the earlier conversation and the memory.
    After a carry-on the reopened session keeps the note, listens, and says the fixed words if the support was never
    heard. A session carrying the call on whose stream fails is treated the same way (seen twice in about 20 such
    sessions: its socket dropped, once 12 s in after it had spoken). One such reopen is allowed per carry-on; a second
    stop or fault, or a session that does not reopen within 30 s, ends the call as `model_failed`, and so does a fault of
    the call's first session, as before (D-124). Never because of the person's words alone. A frame that fails as OpenAI
    drops its socket after a stop waits up to 1 s for the stop to be read before it counts as a fault (found live:
    without it the reopen itself failed). No tool output is sent to a stopped session (OpenAI refuses it).
  - **Earlier conversation:** before a call starts, every start message (the person's and the assistant's) goes through
    the phrase screen, then the rest through one batched moderation check (`CheckManyAsync`, at most 2.5 s). Both run,
    with the family context, while GPT-Live's WebSocket connects: the voice port gained an `OpenAsync` that takes the
    request still being prepared, and the OpenAI adapter connects first. Measured: the check took 0.7 s warm and 1.9 to
    2.6 s cold, the connect 1.5 to 3.4 s; cut at 1.5 s before the overlap, a cold check let a flagged line through twice,
    and the reopen below caught it at a cost of 3 to 4.5 s and the whole history. Flagged lines are held back and replaced
    by `AssistantPrompt.WithheldHistoryNote` (measured: accepted; the call greeted back gently); they raise no alert,
    because they were said before. A line neither check flags still reaches GPT-Live and is caught by the reopen.
  - **Privacy:** unchanged. Only fixed codes are logged and counted (`azwah.consultant.resumes` by `person`, `seeded`,
    `fault` or `limit`; `model_calls` kind `crisis`; an OpenAI error without a fixed code is now logged by its type,
    such as `error_type_invalid_request_error`); the person's latest line is held in memory only to write the reply,
    and goes to the same provider that already heard it. Restarts are counted, not audited: it is the same call.
- **Consequences:** Measured on 2026-11-10 by the live evaluation (D-127,
  `evaluations/2026-11-10-consultant-crisis-continuity.md`), 12 stops in English and Arabic with the final design: the
  filter stops a session a median 3.8 s after the person stops speaking (2.2 to 5.6 s), after 0.3 to 2.3 s of
  GPT-Live's own supportive opening; the reply is written in 1.4 to 3.3 s (always in time); the new session listens
  2.2 to 4.1 s after the stop; support is heard a median 4.3 s after the stop (3.8 to 6.1 s), 7.4 to 10.4 s after the
  person's last word, while SOS and the crisis card are on screen from the stop. Every new session accepted the note,
  said the words as given and carried the conversation on. The fault reopen is verified by scripted tests only. The
  supportive instructions, the fixed and closing words and their Arabic (masculine
  forms of address) are **engineering wording pending the owner's and a clinical review**; whether a qualified reviewer
  must approve them, and the cap, stay in open question 53, the Arabic in open question 58. A model, not a person, is
  what a family in crisis hears on a call (T-36). The phone app is unchanged (no protocol change, no native rebuild).

## D-142 A production look for the phone: shorter copy, no demonstration labels, sign-in chosen in the app, steady buttons

- **Context:** The owner tried the app on a 360-point Android phone on 2026-11-10 and asked for four things: much less
  text ("60 percent less words"), a look like a production app without the notes about OpenAI, demonstrations,
  simulation, staging or development (while it stays a demonstration underneath), choosing UAE PASS staging inside the
  app instead of on the identity provider's page, and buttons whose labels no longer split over two lines or sit out
  of line. A first version kept the labels behind a switch; on 2026-11-11 the owner asked for them to go outright:
  "dont keep any notes, no real family is ever going to be using it. its only for a demo which will be screen
  recorded and sent to the judges."
- **Decision:**
  - **This build is for a recorded demonstration on synthetic data only.** The phone's screens no longer label what
    is simulated, staged, not yet approved or sent to OpenAI; the docs do (`demo-guide.md`, "What is simulated";
    `apps/mobile/README.md`). Removed: the "Development mode" banner of every fixtures screen (also on the lock and
    welcome screens); on sign-in the "Simulated sign-in" badge, the "Development sign-in" notice, the "UAE PASS
    staging environment" line and the simulated hand-over's "nothing goes to UAE PASS" line; on the fixtures' account
    choice its "UAE PASS (simulated)" title and "Not UAE PASS" notice; in Settings the "Simulated sign-in" badge,
    "(staging)" after "Signed in with UAE PASS", and the "Development tools" section with its note, "Simulate
    expired session" and the component gallery; on confirm-details the "Simulated" source and "(staging)"; the server
    connection card on Help and on the welcome screen of a development build; the support-contact placeholder on
    Help; the "Demo household" notice beside the demo family button and the "Demo household created" notice after it;
    the "Demo: fill in answers" card and its explanation around Low / Medium / High; "Arabic awaiting approval" on a
    questionnaire's introduction and its questions; the consent text's "awaiting legal review" notice; the fixtures'
    privacy note ("Development mode: nothing leaves this phone…"); "Scoring rules await approval", "Score meanings
    aren't written up yet" and "Not shown until result sharing is approved" on the result; the notes that answers
    and the score go to OpenAI's AI model, before the report is written as the person answers and before "Create my
    report"; the model's name on the report and its PDF; and the simulated notices of the assistant and the
    consultant. Their translation keys are deleted in both languages.
  - **The demo actions stay, as ordinary quiet controls with short neutral names and no explanation:** **Low**,
    **Medium**, **High** (text buttons above the question; screen readers still hear what each fills in), **Auto-fill**
    under **Start** on "Set up your household" (the roster then says "Household set up. Check, then confirm."),
    **Reset data** under Sign out in Settings (fixtures only; "Reset all data?" asks first; "Data reset." on the
    sign-in screen after), and **Other sign-in options** under the UAE PASS button (the provider's own page with its
    test accounts). Their gates are unchanged: fixtures mode, `EXPO_PUBLIC_DEMO_TOOLS`, `__DEV__` and the server's
    `DemoTools:Enabled`.
  - **Nothing claims more than before.** No "production", "verified" or "approved" was added. The fixtures' sign-in,
    which is simulated, reads a plain **Sign in** and **Choose an account** and never names UAE PASS, and shows no
    UAE PASS hand-over. A UAE PASS staging sign-in reads "Signed in with UAE PASS". The report still says it was
    written by AI and may be wrong. The fixture "server"'s own replies (the assistant's and the report's) still say
    they are simulated: they are what that stand-in answers, and hiding them would make the app claim an AI answered.
  - **Copy.** The phone's English copy was cut from 6,601 to 3,040 visible words (54% fewer; screen-reader-only
    strings excluded; `apps/mobile/scripts/copy-word-count.mjs --base <ref>` counts per group), the Arabic from 5,797
    to 2,951 (49% fewer). Explanatory paragraphs became one line or went; buttons say one to three words. Errors and
    offline states still say what happened and what to do; emergency and crisis text, consent purposes and the
    privacy notices keep their substance. The Arabic was shortened by machine and needs a native review.
  - **Sign-in in the app.** The sign-in screen offers one **Sign in with UAE PASS** button (it also creates the
    account; the separate "Create an account" button went), which sends `idp_hint=uae_pass` with that request only,
    so the browser opens on UAE PASS itself. A development build against a development identity provider also offers
    **Other sign-in options**, which opens the provider's own page without the hint. `EXPO_PUBLIC_OIDC_IDP_HINT` is
    now an override: empty means the default above; a value (for example `uae_pass`) sends every sign-in with that
    hint and offers no other sign-in; `none` sends no hint and the screen names no UAE PASS. A test-account session no
    longer ends a UAE PASS session at sign-out.
  - **Buttons.** `AppButton` keeps its label on one line up to 130% system text (shrinking it to at most 85% before it
    would break) and lets it wrap above that, never clipping; font scaling is never turned off. `ButtonRow` shares a
    row out in equal widths with compact padding and stacks the buttons when they would not fit (narrow phones,
    long Arabic labels, large text). Rows of buttons use it, and buttons are no longer shrunk to their words.
- **Consequences:** The labels and the OpenAI disclosure must be restored, with the disclosure in the consent and
  privacy text, before any real family uses the app (open question 69, threat T-35; open questions 48 and 52 still
  stand). CONTRIBUTING.md's rule that simulated behaviour is labelled in the UI no longer describes the phone; its wording
  is for the owner to amend. Tests that matched the old copy or asserted the removed labels were updated, and tests
  check that the demo actions still work without them.

## D-143 One tap of Low, Medium or High finishes the questionnaire

- **Context:** The owner, testing on a phone against the Development API on 2026-11-11: the demo buttons "take a lot of
  time. the data must already be pre seeded and the questionaire should be done instantly when clicked on", and
  confirmed the target: one tap fills every answer, submits and lands on the result, with no review or submit step.
  Measured on the old path (a local API, warm): the server's plan 30 ms, saving 39 answers 39 ms, submitting and scoring
  67 ms, the result 49 ms (158, 134, 321 and 134 ms on a cold API). The time was on the phone: 39 answers chosen one after
  another, each sealed and written to its database and re-rendered, then a flush, the review, Submit and its
  confirmation, and the final save check before submitting (0.88 s with nothing to save, D-113), so the shortcut also
  stopped short of a result: leaving the review left the survey in progress (D-146).
- **Decision:**
  - `POST /api/v1/sessions/{sessionId}/demo-submission` with `{"level": "low"|"medium"|"high"}` and an
    `Idempotency-Key` (`SubmitDemoAnswers`, policy `SessionParticipant`, rate limit `participant-writes`). The server
    chooses the answers with `DemoAnswerPlanner` from the single approved scoring rule set (D-130), saves them through
    `SaveResponses` and submits through `SubmitSession`: the same use cases, session lock, consent and expiry checks and
    separate commits (answers; then submission, score and routing) as a person's own answers. Nothing is scored or chosen
    on the phone, and nothing new runs the engine.
  - **Pre-seeded:** a checked plan is kept per approved rule set, scope and level for the life of the API
    (`DemoAnswerPlanCache`); an approved rule set and its version never change (D-25), and the planner still finds the
    approved rule set first, so a retired one's plan is never used.
  - **Idempotent and refused like answers:** the key is the submission's, so a retry with it answers as the request that
    submitted did; a session submitted by any other request is 409 `sessions.not_in_progress` and keeps its answers; an
    expired assignment is 409 `assignments.assignment_expired`; missing consent 403; no approved scoring 409
    `scoring.policy_not_approved`; another family's session 404; off unless `DemoTools:Enabled` (404
    `demo_tools.disabled`). The saved answers are audited as `sessions.demo_answers.saved`, beside the submission's own
    `sessions.session.submitted`.
  - **Phone:** the three buttons call it and open the result over Home (`useLeaveFlow`), then drop the session's local
    copy and refetch every participant read, as after a queued submission (`AssessmentEngine.submittedOnServer`). They
    keep their busy state, stay off while a submission waits or the time to answer has passed, say "No connection. Try
    again." offline (sending nothing) and "Couldn't finish. Try again." on a refusal; a session the server had already
    closed opens its result. Fixtures do the same through the fixture API's `submitDemoAnswers`.
  - `GET …/demo-answers` stays, unused by the phone, for measurements that save a band's answers without submitting.
- **Measured (local API, warm):** from the tap's request to the result read, 0.21 to 0.22 s (submission 0.17 to
  0.18 s, the session and result reads 0.04 s in parallel); 0.53 s on the first call after the API started, through
  the phone's own API client; 2.8 s for the very first request a fresh API process serves (start-up compilation). The
  written report never blocks the score: it is written on the result screen afterwards (D-145). Not measured on a phone:
  the phone now makes one request instead of 39 local writes, a flush, a review and two taps.
- **Consequences:** the demo buttons no longer show the review, so a demonstration cannot show it from them; it is still
  there for answers typed by hand. T-30 and T-37 cover the switch.

## D-144 New survey: a demo-only round of its own for the family, so a second result adds a point to the trend

- **Context:** The owner: "I must be able to take the questionaire right again in order to show the chart of progress
  of the family's resilience", and confirmed a demo-only action that opens a new check-in round at once. A wave assigns
  each member once (`ux_assessment_assignments_live`), roster confirmation refuses when two waves are open
  (`assignments.wave_ambiguous`), and the demonstration database has one closed and one open wave for 60 households.
- **Decision:**
  - `POST /api/v1/assignments/demo-check-in` (`OpenDemoCheckIn`, policy `Participant`, rate limit `participant-writes`,
    household managers only, roster confirmed, `DemoTools:Enabled`). It makes a wave for the tap (`demo_wave_<n>`, the
    series the demonstration database starts, opening now and closing in seven days) and **leaves it Planned**: nothing
    that looks for the open wave (roster confirmation, every other household) ever sees it. It then runs
    `IHouseholdAssignmentService` for the caller's household only, so who answers is decided by the approved eligibility
    rules, as at roster confirmation; nothing is assigned by hand. No other household is assigned in it and nothing
    else is closed or changed. The response names the household assessment assignment to answer.
  - **The check-in is the household assessment's**, the questionnaire the trend draws (D-132): while one the caller may
    answer is waiting or in progress, unexpired, it is returned instead (`opened: false`), so a retry or a second tap
    opens the same one; a module open beside it does not count. With nothing to assign the transaction rolls back, the
    wave with it (409 `demo_tools.nothing_to_assign`); an unconfirmed roster is 409 `demo_tools.roster_not_confirmed`.
    Audited as `assignments.demo_check_in.opened` on the household.
  - **The trend needs no change:** points are per submitted session, ordered by submission time and then session, and
    the phone spaces them evenly by index (`trendGeometry.xOf`), so results minutes apart draw as separate points, each
    labelled with its round's code. No migration.
  - **Phone:** with demo tools on, a quiet **New survey** text button under the result's buttons and under the
    resilience chart opens the round, starts its session and opens the questionnaire over Home, where Low, Medium or
    High finish it (D-143); every participant read is refetched, so the chart shows the new point at once. Offline and
    refusals say so in a line. Fixtures do the same (`openDemoCheckIn`, rounds `dev-wave-<n>`).
- **Effect on other data:** each round adds one wave, one assignment, one session and one result for this household:
  the dashboard's wave filter lists the wave, and its counts for "all waves" go up by that one result, as they would
  for any answered round. Nothing of any other household, and no seeded result, changes; `demo:reset` removes them.
  Measured on a local API: three rounds for the lead gave the trend 127, 98, 137, 58, 98 across `demo_wave_1` to
  `demo_wave_5` with no restart; each round took 0.08 to 0.10 s to open and 0.04 s to start (1.6 s cold).
- **Consequences:** threat T-37. The phone shows a round by the number at the end of its wave code ("Round 3"), on the
  trend as everywhere else (`roundOf`); a code with no such number would still show as it is (open question 70).

## D-145 A compact report: two strengths, two weaknesses, three steps, word budgets the server enforces

- **Context:** The owner: "The report is too big after the end of the questionaire, its needs to be precise and compact
  and 80 oercent shorter than what is right now." Measured live with synthetic answers (English, words on screen):
  written as the person answers, 1,730 (low), 1,732 (medium) and 989 (high), of which the model wrote 702, 761 and 498
  and the knowledge graph the rest (up to 21 services with their procedures and documents); the whole-report call 1,337
  to 1,619. Ready 5.4, 3.6 and 3.8 s after the last answer; the whole report 9.7 to 14.1 s.
- **Decision:**
  - **Prompts:** short plain sentences, no repetition, no disclaimers (the app adds its notice; the SOS rule still
    applies), nothing about the scale. A summary of at most two sentences and 30 words; titles of 6 words, details and
    reasons of 16, areas of 6, one or two steps of 12; a part at most one strength, weakness and step; a whole report two
    strengths, two weaknesses and three steps, weakest first. No entry may say there is none.
  - **Enforced after the model answers** (`ScoreReportWordBudget`): every text is cut to its budget at the last whole
    sentence that fits (an ellipsis only when the first sentence is longer than the budget), lists to their counts, a
    whole report's steps to the three weakest after they are ordered by where their areas stand, and each emirate to one
    service (`MaxServicesPerCoverage` 1; D-115's rule that every emirate keeps a public service stays).
  - **Which areas:** a part now says where its area stands (`standing`, 0 to 1, as a step's does, D-114); the phone keeps
    the strengths of the two areas standing highest, the weaknesses of the two lowest and the three lowest steps. It
    reads no answer and computes nothing: the order is the server's.
  - **Organisations, compactly:** each shows its symbol, name, where it serves, a fee note if private, the services the
    graph reached by name with their own pages, its website and phone, and its check date; purpose, need, "apply
    through", procedure and documents stay on All services and the service's page. One line for the whole flow says to
    confirm before going, that Azwah contacts and shares with no one, and that they decide. The PDF follows the screen.
  - **Kept:** the model never scores or decides eligibility and writes only from the recorded result's answers; the
    report is marked "Written by AI", now once, by its badge (the caption under the title is the date; the PDF's line
    still says so); crisis handling and the safety notice are untouched; tests use `RecordingScoreReportModel` only.
- **Measured after (live, same synthetic answers):** on screen 209 (low), 204 (medium) and 149 (high) words, 85 to
  88% fewer; the model's own words 172, 167 and 125 (75 to 78% fewer); ready 5.4, 2.8 and 2.1 s after the last answer,
  every part in parallel; the whole report 171 to 201 words in 4.0 to 8.6 s. No model text named an organisation or
  quoted a score. The live evaluation (D-127) has no report suite, so this measured run stands in for it.
- **Consequences:** a family reads less of each organisation's procedure in the report itself (open question 71). A
  report built from parts saved before this change is written again (its parts lack `standing`).

## D-146 Home shows the survey done and the family's next steps

- **Context:** "even after I was done with the survey by clicking in the auto fill button, when I returned to the home
  screen the screen still showed that I still need to take the survey in order to show my family's next steps."
  Reproduced with the app's own Home, engine and API client: in API mode against a local demonstration database, and in
  fixtures mode with the app's catalog. Three causes, none of them stale caching (Home's reads were refetched within the
  same second of a submission):
  1. The demo buttons stopped at the review; until Submit and its confirmation the session was in progress, so Home
     rightly said "Continue survey" (fixed by D-143).
  2. In fixtures the round also holds the codebook's Module A, which the demo buttons cannot fill (no scoring proposal,
     409 `scoring.policy_not_approved`), and Home counted the survey done only when every questionnaire was: after the
     household assessment it still said "Answer the family survey · Now · Start survey".
  3. Home's next steps came only from routed recommendations (D-100), and no referral rule is approved anywhere (none in
     the demonstration database, none in fixtures). After a completed survey the path sat on "See your next steps · Now"
     with nothing to press, the section said "No support routed yet." and the survey step still said "your next steps
     depend on it": the family's next steps were in the report (D-114), which Home never read.
- **Decision:**
  - A participant's assignment carries `instrumentKind`. The path's family survey is the household assessment, whose
    result the next steps and trend come from (D-105); a family-voices module open beside it stays under "All
    questionnaires" and does not hold the path back; a family given only modules counts them all, as before.
  - The survey step says it is the key step only until it is done.
  - The next-steps step opens the latest recorded result (the trend's last point, head of household only) with **See
    next steps**, and is done once routed support exists or the latest result's report, sealed on this phone, holds next
    steps. Home then lists them by area with their reason and **Open report**, marked "Written by AI"; before a report is
    written it says "They're with your result." with **See result**. Routed support still comes first, as recorded.
    Saving a report refreshes Home; "Your family resilience" shows the same.
- **Measured live (API mode, the app's code):** after New survey and one tap, Home showed "Answer the family survey ·
  Done" and "See your next steps · Now" with See next steps, without a refresh.
- **Consequences:** Home may now show a model's next steps where D-100 showed only rule-routed support; they are the
  report's, marked as AI, and nothing is routed or referred (open question 72).

## D-147 Google sign-in beside UAE PASS, through the Azwah identity provider

- **Context:** the owner, 2026-11-19: "I want Google SSO integrated into the system along with the already existing sign
  in options, dont touch any other functionality, the system has to work as it is." Every client already signs in through
  the Azwah identity provider (D-129), which federates to UAE PASS staging and, in Development, offers personas.
- **Decision:**
  - Google is a second **upstream** of the identity provider, built like UAE PASS: `idp_hint=google` on `/authorize`
    sends the browser to Google (authorization code with PKCE S256, a fresh single-use state, `prompt=select_account`
    so a shared phone always picks an account, `hl` in the app's language); `GET /google/callback` exchanges the code on
    a bounded back channel (15 s, 64 KiB, no request logging), reads Google's userinfo, drops Google's tokens, and issues
    the provider's own single-use code exactly as before. The API, the admin dashboard and the token format are
    unchanged; the API never talks to Google.
  - The subject is `google|{sub}` (Google's stable account ID); the role is `participant`, as for UAE PASS. The access
    token carries no personal claim. The ID token adds `name`, `given_name`, `family_name` and `azwah_profile` with
    `source: "google"` and `email_masked`, only when Google says the address is verified.
  - Pending upstream sign-ins remember which upstream they went to: a state issued for UAE PASS is refused at the Google
    callback and the reverse.
  - Discovery lists the configured upstreams as `azwah_identity_providers` (`uae_pass`, `google`). The phone shows
    **Continue with Google** (Google's white button and "G" mark) under its main sign-in button only when the issuer lists
    `google`, so no build claims a sign-in that is not configured (rule 8). The persona page in Development shows
    "Continue with Google" beside "Continue with UAE PASS (staging)". The confirmation screen says "Google account", never
    UAE PASS. Sign-out sends no UAE PASS hint for a Google session; Google offers no end-session for this kind of
    client, so the account chooser at the next sign-in is what protects a shared phone.
  - Off by default and with no credentials in the repository: `IdentityProvider:Google:Enabled`, `ClientId` and
    `ClientSecret` from the owner's own Google Cloud OAuth client (user secrets, environment or `.env`); enabled without
    credentials stays off with a warning in Development. Endpoints must be https on Google's own hosts. A deployed
    provider (any environment but Development) also needs `IdentityProvider:Google:ApprovalReference`: Google accounts
    are real people, not staging test identities (open question 73). Deployed, requests without a hint still go to UAE
    PASS, which stays required there.
  - Staff roles are unchanged: a Google sign-in is a participant, so the admin dashboard refuses it like a UAE PASS one.
- **Not configured here:** no Google OAuth client exists for Azwah yet. Every test uses a stub of Google; no test calls
  Google. Until the owner creates a client and sets its ID and secret, the Google button does not appear anywhere.
- **Consequences:** a person can hold two platform accounts (one per upstream) if they sign in with both; linking them is
  not offered. Production use is open question 73.

## D-148 A shareable demonstration APK against the owner's laptop

- **Context:** the owner wants to send one sideloadable release APK to other people. It talks to the owner's laptop,
  which runs the API and the Azwah identity provider in **Development** (the demonstration database, synthetic data
  only, D-140) behind a public HTTPS tunnel (Tailscale Funnel). A release build (`__DEV__` false) refused that setup:
  discovery refuses an issuer that says `azwah_development_only` (D-82), and the persona page ("Other sign-in options",
  D-142) and the demo family ("Auto-fill", D-131) were offered only in development builds. A development build is not
  an answer: it needs Metro, and carries the fixtures and the development-only screens.
- **Decision:**
  - A build-time opt-in, `EXPO_PUBLIC_DEMONSTRATION_BUILD=true` (`resolveDemonstrationBuild` in
    `apps/mobile/src/config.ts`, read by name so Expo inlines it; anything but `true`, in any case and trimmed, is off).
    It is a setting of its own (`demonstrationBuild`), threaded like `isDevelopmentBuild` (`ServiceDependencies`,
    `resolveOidcConfig`, `OidcConfig`), never folded into `isDevelopmentBuild`.
  - In a release build it enables **only**: accepting an issuer whose discovery says `azwah_development_only`
    (`acceptsDevelopmentIssuer`); offering **Other sign-in options**, the provider's persona page, when that issuer is
    development-only and no `EXPO_PUBLIC_OIDC_IDP_HINT` is set (the provider's `acceptsDevelopmentIssuer` replaces the
    `__DEV__` check); and **Auto-fill** on "Set up your household".
  - It does **not** enable plain `http` for the API or the issuer (`resolveApiBaseUrl`, `isAllowedUrl` and discovery's
    endpoint check stay HTTPS-only in release, which the tunnel provides), fixtures mode (`resolveAppMode`), the
    component gallery, or Settings' fixture **Reset data**. Low / Medium / High and New survey keep their own switch,
    `EXPO_PUBLIC_DEMO_TOOLS` (D-130, D-144), and the server's `DemoTools:Enabled` (Development only).
  - Nothing on screen says it is a demonstration build (D-142); no text was added. The build steps are in
    `apps/mobile/README.md`, "Shareable demonstration APK"; the APK is signed with the debug keystore, for sideloading
    only, never a store.
- **Risks:** anyone with the tunnel's address can sign in as a persona (the Development provider checks no identity)
  and use the demonstration data; only synthetic data may be on that stack (rule 15). Every assistant answer,
  consultant call and report spends the laptop's OpenAI key: the owner sets a spending cap on it before sharing. The
  laptop serves whoever has the address while the tunnel is up; stop the tunnel when the demonstration ends.
- **Consequences:** the flag must never be set for a build real families use: such a build would accept a development
  provider that mints any persona's token, on top of the missing labels and OpenAI disclosure (open question 69, threats
  T-35 and T-39).

## D-149 Google staff sign-in to the admin dashboard, on the demonstration stack only

- **Context:** the owner, 2026-11-20, wants any Google account that can sign in to also use the admin dashboard as staff on
  the shareable demonstration stack (D-148: Development, synthetic data, behind Tailscale Funnel). Until now a Google sign-in
  was always `google|{sub}` with `participant` (D-147), so the dashboard showed it nothing, and staff signed in with the
  Development personas only. The owner's Google OAuth client is in *Testing*: Google lets only its listed test users sign
  in.
- **Decision:**
  - Two settings of the identity provider, both off by default: `IdentityProvider:Google:StaffRoles` (a list of role
    codes) and, per client, `IdentityProvider:Clients:<id>:GoogleStaffSignIn` (explicit, never inferred from the client
    being confidential). When both are set, a Google sign-in **for that client** gets the subject
    **`google-staff|{sub}`** and the listed roles instead of `google|{sub}` and `participant`. The phone's Google
    sign-in (`azwah-mobile`) is unchanged; persona and UAE PASS sign-ins are unchanged.
  - A subject of its own, because the API keeps one role snapshot per subject and audits every difference as
    `identity.role.added`/`removed` (D-81): with one subject, the same person signing in on the phone and on the
    dashboard would flip between `participant` and staff roles on every request and mix a household's membership with
    staff permissions. The two accounts never share records.
  - **Development only.** Outside Development start-up refuses a non-empty `StaffRoles` and any client with
    `GoogleStaffSignIn`, and the provider also checks the environment when it builds the grant. In Development it refuses
    a role the platform does not know (only `system_administrator`, `program_administrator`, `researcher`,
    `support_coordinator`; a test keeps this list equal to the API's `Roles.All` without `participant`), refuses
    `participant`, and refuses `GoogleStaffSignIn` on a public client (staff sign-in is for the confidential dashboard
    client, never the phone).
  - Several roles work end to end: the token's `role` claim is already an array; the API's policies each ask for any of
    their roles and `GET /api/v1/me/permissions` grants the union; the dashboard shows sections by those permissions, not
    by a single role. The owner may list all four staff roles.
  - The persona page labels "Continue with Google" for an opted-in client as "signed in as staff with roles: …" (it
    already offered Google to every client, as a participant, since D-147). Start-up logs a warning (event 3015) naming
    the client and the roles while it is on.
  - This amends the last point of D-147 for Development only; deployed, a Google sign-in is still a participant.
- **Risks:** the gate is **the Test users list of the owner's Google OAuth client**, not Azwah: anyone on it is a
  system administrator (or whatever is listed) over the demonstration data, including scores and safety alerts; Google's
  consent screen published to *In production* would let every Google account in. The accounts are real people (open
  question 73) with no MFA or staff identity checks of Azwah's own (open question 37). Synthetic data only on this stack
  (rule 15); never for real families or a deployed environment. Threat T-40.
- **Consequences:** removing a person means removing them from the Google test users and restarting the provider (its
  refresh tokens live 8 hours in memory). Staff accounts made this way appear in the API's role snapshots and audit log
  as `google-staff|…` subjects of the demonstration database.

## D-150 The public repository is a clean snapshot of the application; the dashboard joins the Compose stack

- **Context:** the owner, 2026-11-20, publishes the application as a new public GitHub repository: one fresh commit of
  the tree, without history, from which anyone can run the whole platform on synthetic data by following the README.
  The private repository holds files that are not the application or must not be published.
- **Decision:**
  - Left out of the public tree: the third-party source documents (`docs/sources/`: the Abu Dhabi Family Survey
    codebook and manual, which are not ours to publish); planning notes (`docs/superpowers/` specs and plans, and
    `docs/evaluations/`), which record local paths and working history; the design mockups (`docs/design/`); the
    earlier investor demo (`apps/demo-web`, with `render.yaml` and its CI workflow), which is independent of the
    platform; and the coding-agent instruction files (`CLAUDE.md`, `apps/*/AGENTS.md`). The rules people need from
    `CLAUDE.md` move to `CONTRIBUTING.md` with the same numbering, which code and documents now cite.
  - The full history, and everything left out, stays in the private repository.
  - Two content tests compared the committed questionnaire content with those sources (`CodebookContentTests` read the
    codebook CSV, `HouseholdAssessmentContentTests` the demo's TypeScript). They now pin the structure explicitly and
    compare a SHA-256 fingerprint of the copied question and answer text, taken when the comparison last passed, so an
    edit to the text still fails a test without the sources. The content files' provenance strings name the sources
    without their paths. The committed content itself (the codebook's questions, verbatim, as an unapproved draft) is
    unchanged and is published: the platform needs it to run.
  - The admin dashboard joins `infra/docker-compose.yml` (an image built from `apps/admin/Dockerfile`), so one
    `docker compose up` runs PostgreSQL, the API, the identity provider and the dashboard. It runs the production build
    with `ADMIN_ENVIRONMENT=development` only so that it accepts the plain-HTTP local issuer, with OIDC sign-in through
    the identity provider's Development personas and the Compose API as its data source. The issuer must be the same
    URL for every client (D-129) and defaults to `http://localhost:5090`, which inside a container is the container
    itself: a small `admin-network` service (the official Node image, a dozen lines of script) owns the dashboard's
    network namespace, publishes its port and forwards the issuer's localhost port to the identity provider. It owns the
    namespace rather than joining the dashboard's, so restarting or rebuilding the dashboard keeps the forwarding. The
    identity provider's return addresses for the dashboard follow `ADMIN_PORT`.
  - `USE_DEMONSTRATION_DATABASE` (default false) sets the Compose API's `Database:UseDemonstrationDatabase` (D-140);
    `docker compose run --rm api db demo` builds the demonstration database first. Exports go to a folder inside the
    API container.
- **Consequences:** a stranger can run everything with Docker and the README; the phone runs from Expo Go in fixtures
  mode, or a development build against the local stack. The public tree has no licence until the owner chooses one
  (all rights reserved). Earlier records still name the files left out (see the note at the top).

## Open questions register

| # | Question | Why it matters | Blocks | Owner | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | Approved wording, areas and reverse-scored items for the 39-item household assessment | The demo's items were drafted, not supplied | Questionnaires, Scoring | Product owner | Open — the demo's draft is in the platform as draft version 1 of `household_assessment` (D-99); approval pending |
| 2 | Approval of the proposed tier bands (39–78, 79–117, 118–156) | Bands drive what families are told | Scoring | Product owner | Closed — the owner approved the cut-offs and the band names for the head of household on 2026-10-28 (D-103); the rule set itself still needs approving in the platform (D-99) |
| 3 | Bands and flags for the three family-voices modules | The source defines only questions and answers | Scoring | Product owner | Open — the manual proposes module bands (scoring-specification §6); directions and aggregation are still missing (27, 28) |
| 4 | Whether the two instruments' results are ever combined | The brief says they must not be assumed interchangeable | Scoring, Analytics | Product owner | Open |
| 5 | UAE PASS onboarding: credentials, environments, claims | Needed for real sign-in | Identity | Product owner | Open — the platform's identity provider (`FamilyResilience.IdentityProvider`) now signs families in with UAE PASS **staging** and the public sandbox client in every environment (D-96, D-129); production onboarding (credentials, registered redirect URIs, official button artwork) is still open; see 37, 55 |
| 6 | Hosting and data residency for participant data | Sensitive data; UAE requirements | Production readiness | Product owner | Open — hosting, encryption-at-rest and secret requirements are recorded in security-model.md (D-85) and the database roles are built (D-86); nothing is selected |
| 7 | On-device draft encryption, and therefore Expo Go versus development builds | Drafts will hold sensitive answers | Sessions | Product owner | Closed for drafts — field-level AES-256-GCM with the key in the platform keystore, in Expo Go and development builds (D-70); a whole-database cipher stays optional |
| 8 | Apple Developer account for iOS builds | Needed for development and store builds on iPhone (and to see RTL on iPhone) | Production readiness | Product owner | Open |
| 9 | Legal review of consent text in Arabic and English | Consent must be valid | Consent | Product owner | Open |
| 10 | Consent and assent for members under 18 | Children appear in households | Consent, Members | Product owner | Closed — adults only: under-18 members are never assigned or asked (D-58) |
| 11 | Who holds an account: every member, or a household lead answering for others | Shapes identity and sessions | Identity, Households | Product owner | Closed — D-49 approved: the manager acts for members without an account (D-58) |
| 12 | Research access: de-identification rules, minimum cell sizes, retention periods | Protects participants in analytics | Analytics | Product owner | Open — minimum group size 10 approved (D-58); export de-identification rules and the 7-day file retention are proposed (D-60); retention periods open |
| 13 | Referral partners, routing rules and what is shared with them | Data leaves the platform | Referrals | Product owner | Open — partners are registered and referrals handed off manually (D-61); nothing is sent electronically; what a coordinator may share awaits partner agreements and the consent review (9) |
| 14 | Final role list and permissions | Drives every authorization policy | Identity | Product owner | Open — proposed list and policies in D-48; HouseholdManager comes from memberships, never from tokens; role changes are audited as observed (D-81) |
| 15 | Final app name, bundle identifiers (provisionally `ae.azwah.mobile`), branded icon and splash | Hard to change after the first store build | Production readiness | Product owner | Open |
| 16 | Module A routing: head of household **or** primary earner (source) versus main earner only (demo) | Changes who is asked about finances | Assignments | Product owner | Open — the codebook also says 'Head of Household / Main Earner'; proposed rule uses OR |
| 17 | Expo Go will soon support only SDK 58: upgrade timing, or rely on development builds | Store Expo Go may stop opening this app | Mobile | Product owner | Open |
| 18 | Approval of the relationship, employment status and consent capture-method value lists | They are proposed by engineering | Households, Consent | Product owner | Open |
| 19 | Whether any name, contact detail or region must be stored, and on which lawful basis | None is stored today | Households | Product owner | Open |
| 20 | Assignment uniqueness per instrument version (built) or per instrument per wave | A new version mid-wave could otherwise be assigned again | Assignments | Product owner | Decided in the engine (D-35); the database stays per version |
| 21 | Whether consent records must be kept as evidence after an erasure request | Decides the erasure order | Consent, retention | Product owner | Open — erasure is built but refuses to run until `Erasure:ConsentRecords=delete` is chosen; keeping evidence is not built (D-88) |
| 22 | Retention periods for participant data and audit events (extends 12) | Nothing is deleted until approved | Retention job | Product owner | Open — draft in retention-policy.md (D-85); the retention job is built, off by default, and has no default periods (D-88) |
| 23 | Persistence for support referrals | Domain types existed without tables | Scoring, Referrals | Engineering | Closed — score results (D-38), referrals (D-45) |
| 24 | Whether a household may be left with no active member or no active head after deactivations | The domain allows it today | Households | Product owner | Open |
| 25 | Whether withdrawing a household must stop new assignments and consent grants for its members (today only the member's own status is checked) | Withdrawal should stop new data collection | Households, Assignments, Consent | Product owner | Open |
| 26 | Which employment statuses count as "employed" for Module B (is `self_employed` included?), and aligning the employment list with the codebook (Employed, Retired, Unemployed, Student, 99) | Decides who answers Module B | Assignments, Households | Product owner | Open |
| 27 | Scoring directions for every item of modules A–C (which items are reverse-scored; Q29 is ambiguous) | No module can be scored until stated in a new version | Scoring | Research owner | Open |
| 28 | How module totals are computed when 99 answers are excluded (prorated mean? rounding?) and whether ">30%" is strict | The manual's sum bands do not say | Scoring | Research owner | Open |
| 29 | Roster questions Q01–Q03: where the primary caregiver (Q03) is stored, and how "99" on age and employment is recorded | Birth month is required today; declined answers have no value | Households | Product owner | Open — the 2026-11-10 audit adds: Q03 has no field anywhere; Q01 exists only as the derived member count; a member who declines age or employment cannot be recorded, which blocks roster confirmation and every assignment; and code 99 collides with a real age of 99 (Q02) and a count of 99 (Q01), so a different non-response code is needed there |
| 30 | Provenance of the codebook, manual and sample-questions documents (they read as AI output): a named research owner confirms content, routing and bands | Approval must rest on a confirmed source | Questionnaires, Scoring | Research owner | Open |
| 31 | Arabic for the missing answer labels, section titles and instrument name, and correction of the Q19 and Q23 Arabic typos | The draft cannot be approved without it | Questionnaires | Content owner | Open |
| 32 | Whether scoring runs automatically after submission (outbox consumer) or stays a staff action | Today staff trigger it | Scoring | Product owner | Closed — scored in the submission transaction (D-52) |
| 33 | The manual's referral pathways (SSA, Ma'an, FCA, SEHA, M42, "automated case transfer"); extends 13 | No integration exists; nothing may claim one | Referrals | Product owner | Open — manual hand-offs only (D-61); no electronic pathway or automated case transfer exists |
| 34 | What a "study cohort" is (a recruitment group, a site, a wave?) and which cohorts are approved | The research brief asks to filter by cohort; the platform has waves only | Analytics | Research owner | Open — the dashboard filters by wave and says cohorts are not defined (D-75) |
| 35 | Comparability rules between instrument versions and between scoring rule sets | Trends across versions would mix incomparable scores | Analytics, Scoring | Research owner | Open — only the same version, module and rule set is compared across waves (D-75) |
| 36 | Which demographic comparisons of scores are approved, and their minimum group sizes | Score breakdowns by group can identify people | Analytics | Research owner | Open — none is shown (D-75); completion by age band and employment status remains on the overview |
| 37 | Which production identity provider(s): UAE PASS for families, a staff directory for the dashboard, or one provider for both; MFA for staff roles; token lifetimes and refresh rotation | Sign-in cannot work in production without it; the mobile and admin clients are provider-neutral | Identity | Product owner | Open — participants: UAE PASS through the Azwah identity provider (D-129); the staff provider, MFA and lifetimes for staff are still open; extends 5 |
| 38 | Who grants staff roles in the identity provider, how often access is reviewed, and whether approving content or rules needs a second person | A rogue grant or approval is detected (D-81) but not prevented | Identity, Questionnaires | Product owner | Open — a staff role appearing raises a critical alert (D-90) |
| 39 | How families ask for a copy of their data, a correction or erasure; who answers and how fast; the legal basis that applies | Families must be able to exercise their rights | Privacy | Product owner, legal | Open — the download, request intake, staff handling and erasure are built (D-88, D-89); who answers, response times, the legal basis and whether scores belong in the copy are not decided |
| 40 | Independent security testing (penetration test) and a data-protection impact assessment before production | The threat model is an engineering review only | Production readiness | Product owner | Open |
| 41 | Who owns the support-resource directory, approves its content and sets the review cadence (366 days proposed) | Nothing can be listed until someone vouches for it, and stale entries mislead families | SupportResources | Product owner | Open — the directory, approval and review dates are built; it is empty (D-92) |
| 42 | Whether an AI-assisted discovery use case is approved: for whom, with which provider, region and data terms | A person's free text would leave the platform | SupportResources | Product owner, legal | Open — the language-model path is built, guarded and dormant; no adapter exists (D-94); extends 6 |
| 43 | The reviewed Arabic and English wording of the information-only notice, and of any "ordered with AI assistance" label | Results must not read as an assessment, a diagnosis or a referral | SupportResources, Mobile | Product owner | Open — the API returns the code `information_only`; no screen shows it yet (D-93) |
| 44 | Which AI use cases are approved (an AI-assisted family plan, the "Ask Azwah" assistant), with which provider and region, under which data-processing agreement, and what the model may see | The owner wants both; no provider is approved and rule 7 forbids AI scoring or eligibility | Referrals, mobile assistant | Product owner, legal | Open — Home shows rule-routed support only (D-100); the assistant screen ships switched off (D-101) |
| 45 | Who keeps the SOS emergency and support numbers current, and how often they are reviewed | A wrong number in an emergency list is a real harm | Mobile | Product owner | Open — each number records its official source and check date (D-102) |
| 46 | Whether the spoken Azwah consultant is approved: the speech provider and region, whether audio or transcripts are kept and for how long, the microphone consent wording, what the model may see, safety handling and evaluation in Arabic and English (extends 44) | A family's voice is sensitive personal data and would leave the phone | Mobile consultant | Product owner, legal | Open — the screen ships switched off and asks for no microphone (D-107) |
| 47 | Whether families must consent before the AI helpers use their family's data and answers (a consent purpose, its wording and withdrawal), which provider and region receive it, under which data-processing agreement, and how long the provider may keep requests (extends 44 and 46) | The owner chose that the AI sees everything about the family; that data leaves the platform once a model is connected | Assistant, Consent | Product owner, legal | Open — OpenAI is connected in Development only, for the assistant and the consultant (D-119, D-120) and for the score report (D-112, see 48); a deployed API with a key refuses to start without an approval reference (open question 52) |
| 48 | Before the written score report reaches real families: the data-processing agreement and region for OpenAI, whether its disclosure is enough or a consent purpose is needed (answers now leave while being given, even if never submitted, D-113), OpenAI's actual retention terms for what the disclosure says, reviewed Arabic and English wording, an expert evaluation of what the model writes in both languages, and a spending limit | It sends a family's answers abroad and tells a family about its weaknesses; a deployed API refuses to start without an approval reference | Assistant, Consent, Mobile | Product owner, legal | Open — live-tested with made-up answers only (D-112, D-113) |
| 49 | Who owns the support knowledge graph: who approves each organisation and procedure, how often they are reviewed (a date is required per entry, with no cadence set), and whether people outside engineering need an admin screen to edit it | A wrong or stale procedure sends a family to the wrong place | Assistant, Mobile | Product owner | Open — the owner named 13 organisations (D-115); nobody owns their upkeep yet |
| 50 | Review of the 13 organisations as researched: what "ACPN" means, whether the Sharjah body should be Social Services, whether Tarmeem stays, the TAMM steps seen only in search summaries, the Arabic translated for the file, and the gaps (debt advice, the northern emirates). List: `docs/support-knowledge-graph.md` | Until then the app tells families to confirm before they go, and every entry's approval reference says it is unreviewed | Assistant, Product | Product owner | Open (D-115) |
| 51 | Whether a report should ask which emirate a family lives in. The platform holds no emirate, so a step can list services from several, each labelled, and most of the 13 serve Abu Dhabi only | Families outside Abu Dhabi see help they can't use, and those in the northern emirates may see none | Assistant, Mobile, Households | Product owner | Open (D-115) |
| 52 | Approval of OpenAI as the provider for the assistant and the consultant: the data-processing agreement, the region where requests are processed, how long OpenAI keeps requests, audio and transcripts (abuse monitoring, zero data retention), spending limits on the key, and an evaluation of both in Arabic and English, recorded as `Assistant:Model:ApprovalReference` (extends 46 and 47) | Family data, answers and voices go to OpenAI once a deployed API has the key | Assistant | Product owner, legal | Open — the adapters work in Development with the owner's key (D-119, D-120); a deployed API refuses a key without the reference |
| 53 | What the AI helpers may do on their own: whether the assistant may search the support directory from what a family has told it (D-92 keeps the directory unlinked from answers), the wording it uses for bands Azwah has not described yet, whether GPT-Live is approved alongside the text model, and what a family hears when OpenAI's own filter stops a call about self-harm | The agent (D-122, D-124) is more useful because it can do these things; each is an engineering call until approved | Production use of the assistant and the consultant | Product owner, clinical reviewer | Open — built and labelled as engineering calls; the evaluation harness (D-127) shows the current behaviour. In part answered on 2026-11-10: the owner decided the call must not be cut but go on with an empathetic, calming consultant (D-141). Still open: the supportive wording (the reply's instructions, the fixed and closing words, their Arabic), whether a qualified reviewer must approve it before families hear it, and the cap of five carry-ons a call |
| 55 | Deploying the identity provider: hosting and TLS termination, custody and rotation of `IdentityProvider:SigningKey:Pem`, persistence of refresh tokens and pending sign-ins for more than one instance, rate limiting and proxy trust, and production UAE PASS onboarding (extends 5, 6) | The provider issues every participant's token; a lost or leaked signing key, an exhausted pending store or a restart that ends every session are production incidents, not development ones | Identity, Production readiness | Product owner, engineering | Open — the provider runs deployed with UAE PASS staging only (D-129); numbers 48 to 51 came with the score report (D-111 to D-116), and 54 is not used in this tree |
| 58 | Clinical and language review of the crisis phrase list (`crisis-signals.v1`) and of the crisis support card's wording in English and Arabic, and whether the lowest score band (`vulnerable`) is the right trigger for a score alert (extends 53) | A missed phrase is a missed person; a phrase that fires on everyday speech teaches staff to ignore alerts; the Arabic copy is engineering wording | Safeguarding, Mobile | Clinical reviewer, native Arabic reviewer, product owner | Open — the list is active as an unreviewed engineering starting list (D-136) and the card says what D-138 records |
| 59 | The safeguarding procedure behind the alerts: who is on the rota and who responds, how fast and out of hours; how staff reach a family when the platform holds no contact details (D-97); whether and how participants are told that alerts exist, with the consent and privacy-notice wording; how long alerts are kept; and whether partners or emergency services are ever contacted | An alert is only useful if someone acts on it; the dashboard names no one and sends nothing; an alert is itself sensitive (T-33) | Safeguarding, Privacy, Consent | Product owner, legal, programme leads | Open — alerts are kept until household erasure and nothing is contacted automatically (D-135); numbers 56 and 57 are not used in this tree |
| 60 | Whether a household manager may move the head-of-household flag once a score exists | The score is shown to the head only (D-103), but a manager can make themselves the head with two roster edits, read the score and move the flag back; the audit events do not say which field changed | Households, Scoring | Product owner | Open — found 2026-11-10; options: refuse the change once a session is submitted, route it through a correction request, or accept the manager's declaration and audit head changes by name |
| 61 | Whether support categories routed from a score may be shown to members who may not see the score | `GET /me/recommendations` and the assistant's family context return category codes to any manager, and each code exists only because a score fell in a band; D-100 shows next steps on Home, D-103 says categories stay hidden | Referrals, Mobile | Product owner | Open — found 2026-11-10 |
| 62 | The age convention for rule thresholds when only the birth month is stored | Counting the birthday as the first of the month admits a member to Module C (60 and over) up to a month early and drops them from Module B (over 55) up to a month early | Assignments | Research owner | Open — the adults-only guard is now strict (D-139); thresholds keep the documented convention |
| 63 | Who Module B is for: the codebook says "Working Parents (25–55)", the proposed rule asks any employed member aged 25 to 55 in a household with a member under 18 | An employed sibling or uncle is asked about "my children" (Q15, Q16, Q18); working parents aged 18 to 24 and 56 to 59 receive neither Module B nor Module C | Assignments | Research owner | Open — found 2026-11-10; the rule set is Proposed, so nothing runs; extends 26 |
| 64 | Inconsistencies inside the sources: Q17's midpoint is "Neutral" where Q09 and Q11 use "Uncertain" on the same scale; Q23 says "Light/Heavy Burden" where Q05 says "Low/High"; the manual's module names differ from the codebook's; Q16's "1–2 hours" and "2–3 hours" have Arabic in the sample document that was not reused; the Arabic of Q22 appears to omit "cultural", Q36 adds "outside my home" and Q25 renders "systemic" as "structural" (the last three need a fluent reviewer) | The platform copies the sources verbatim and cannot choose between them | Questionnaires | Content owner, research owner | Open — found 2026-11-10; extends 30 and 31 |
| 65 | How the manual's rule on code 99 applies to people: it speaks of "a household" triggering 99 on more than 30% of a module, but modules are answered by each member, and several members may answer B or C | Decides whether "Incomplete / Private" is per person or per household, and whether the 30% is of all module items or of scored items | Scoring | Research owner | Open — found 2026-11-10; extends 28 |
| 66 | Who answers the household assessment: no eligibility rule is proposed for it anywhere in the sources | Without one it can never be assigned, even after approval | Assignments | Product owner | Open — found 2026-11-10; the phone's fixtures and the demonstration database assign it to the head of household, labelled as a demonstration rule (D-140) |
| 67 | A participant-facing name and description for the family survey, in both languages | The only description in the sources is the codebook's document subtitle ("39-Question Adaptive Skip-Logic Structure for Emirati Extended Households"), which is not written for families | Questionnaires, Mobile | Content owner | Open — the app hides a draft version's description (D-139); extends 31 |
| 68 | Whether a hidden count may be recoverable by comparing filters: "all waves" minus one wave gives the other wave's cell, even when that cell is hidden | Suppression is applied per response, so two responses can be subtracted; D-58 hides cells covering fewer than 10 people | Analytics | Research owner | Open — found 2026-11-10 in review; it predates D-139, which fixed the unit counted, not this; options: hide a cell in every filter once it is hidden in one, or round counts |
| 69 | Restoring the phone's labels before any real family uses it: the OpenAI disclosure (in the consent and privacy text and before the score report), the staging and simulated sign-in labels, and the not-yet-approved notes that D-142 removed for a recorded demonstration | Without them a family is not told that answers go to OpenAI, and reads simulated sign-in, staging UAE PASS and draft content as real and approved | Production readiness, Consent | Product owner | Open — removed at the owner's direction on 2026-11-11 (D-142, T-35); the previous wording is in the git history of `apps/mobile/src/i18n/resources`; numbered 69 on this branch, renumber at merge if taken |
| 70 | Whether the phone should show a round as its wave code ("Round demo_wave_3") or as a number or date | Wave codes are internal; the demonstration database's begin `demo_`, and each new survey (D-144) adds one, so a recording shows "demo" in the trend and history although D-142 removed every demonstration label | Mobile | Product owner | Open — since 2026-11-11 the trend shows the number at the end of the code ("Round 3"), as the other screens already did (`roundOf`, D-142, D-144); a number or a date is still the owner's choice |
| 71 | Whether the report may leave each organisation's procedure and documents to All services and the service's page | D-114 put "the procedure for the whole process" in the report; the compact report (D-145) shows names, coverage, contacts and service pages only, to be about 80% shorter | Mobile, Assistant | Product owner | Open — compact since 2026-11-11 (D-145); the full entries are unchanged on All services |
| 72 | Whether Home may list the report's next steps (a model's words, marked as AI) when no referral rule routes support | D-100 showed only rule-routed support on Home; with no referral rule approved anywhere, Home had nothing to show after a survey (D-146) | Mobile, Referrals | Product owner | Open — shown since 2026-11-11, routed support first (D-146) |
| 73 | Whether real families may sign in with a Google account, and with what approval | Google accounts are real, unverified identities (unlike UAE PASS, which the state verifies); the platform must decide whether they may hold participant records, whether one person's Google and UAE PASS accounts are ever linked, and who approves a deployed Google client | Production readiness, Identity | Product owner | Open — Google sign-in exists since 2026-11-19 (D-147), off by default; a deployed provider refuses it without `IdentityProvider:Google:ApprovalReference` |
