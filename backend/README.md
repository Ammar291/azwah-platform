# Azwah API

ASP.NET Core 10 modular monolith (projects `FamilyResilience.*`). Rules and conventions: [`CONTRIBUTING.md`](../CONTRIBUTING.md)
and [`docs/architecture.md`](../docs/architecture.md).

## Run with Docker Compose (database, API, identity provider and dashboard)

From the repository root (the root README's "Quickstart" has every step, including the demonstration database):

    cp .env.example .env        # then fill in the secrets it names
    npm run infra:up            # http://localhost:8080/health/ready, dashboard at http://localhost:3000
    npm run infra:down

## Run with `dotnet run` (API on this machine, database in Compose)

    docker compose --env-file .env -f infra/docker-compose.yml up -d postgres
    cd backend/FamilyResilience.Api
    dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=azwah;Username=azwah;Password=<POSTGRES_PASSWORD from .env>"
    dotnet user-secrets set "Database:ApplyMigrationsOnStartup" "true"
    dotnet run                  # http://localhost:5080, bound to all interfaces for phones on the same Wi-Fi

OpenAPI (Development only): `http://localhost:5080/openapi/v1.json`.
The committed contract is `backend/openapi/v1.json`; `OpenApiContractTests` fails when the API and the file differ. After
a deliberate API change, regenerate it with `AZWAH_UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --project
FamilyResilience.IntegrationTests --filter-class FamilyResilience.IntegrationTests.OpenApi.OpenApiContractTests`, review
the diff and commit it.

## Identity provider (UAE PASS staging; simulated personas in Development)

`npm run identity:dev` (or the `identity-provider` Compose service) runs the platform's OpenID Connect provider,
`FamilyResilience.IdentityProvider`, on `http://localhost:5090` (D-129). In Development it offers UAE PASS **staging**
(when the public sandbox client is set in its user secrets or `.env`; production UAE PASS is not onboarded and is
refused) and the simulated personas, where you pick a test person and no identity is checked. Deployed (any other
environment) it offers UAE PASS only, with no personas and no development markers, and refuses to start without an
`https` issuer, a signing key and the UAE PASS credentials. It is never part of the API image. To make `dotnet run`
trust it as well as `dotnet user-jwts`:

    cd backend/FamilyResilience.Api
    dotnet user-secrets set "Authentication:Schemes:Bearer:Authority" "http://localhost:5090"
    dotnet user-secrets set "Authentication:Schemes:Bearer:RequireHttpsMetadata" "false"
    dotnet user-secrets set "Authentication:Schemes:Bearer:ValidAudiences:1" "azwah-api"

With an authority set, the API fetches the provider's keys on the first bearer request, so the provider must be
running. Details: [`FamilyResilience.IdentityProvider/README.md`](FamilyResilience.IdentityProvider/README.md).

## Development tokens

    cd backend/FamilyResilience.Api
    dotnet user-jwts create --role participant
    curl -H "Authorization: Bearer <token>" http://localhost:5080/api/v1/me

The signing key lives in your user secrets, so these tokens work only with `dotnet run`, not with the container.

Roles (proposed, D-48): `participant`, `researcher`, `support_coordinator`, `program_administrator`,
`system_administrator`. A participant enrols with `POST /api/v1/households`; what they may reach afterwards comes from
their household membership, not from the token. `GET /api/v1/me/permissions` shows what a token's policies allow.

## Settings

| Key | Default | Meaning |
| --- | --- | --- |
| `BackgroundJobs:Enabled` | `true` | Runs the outbox dispatcher and the maintenance service (the test host sets `false`) |
| `Outbox:PollIntervalSeconds` | `5` | Wait between polls when no outbox message is due |
| `Outbox:MaxAttempts` | `5` | Attempts before an outbox message is marked Failed |
| `Maintenance:IntervalMinutes` | `60` | How often expired idempotency keys are purged, expired export files deleted, assignments past their expiry marked Expired (a session still in progress on one is abandoned) and the retention job run |
| `Retention:Enabled` | `false` | Runs the retention job (D-88); nothing is deleted on a schedule unless this is `true` and a period is set |
| `Retention:WithdrawnHouseholdDays` | none | Erase households withdrawn longer than this (1 to 36500 days; needs `Erasure:ConsentRecords`; at most 50 a run) |
| `Retention:ProcessedOutboxDays` | none | Delete processed outbox messages older than this |
| `Retention:FinishedExportJobsDays` | none | Delete failed and expired export jobs older than this |
| `Erasure:ConsentRecords` | none | Only `delete` lets erasure run: consent records go with the household. Anything else is refused as not built (open question 21) |
| `ConnectionStrings:Migrations` | none | The schema owner's connection for `db migrate` and start-up migrations; when unset, `Default` is used (D-86) |
| `Database:UseDemonstrationDatabase` | `false` | Development only: run against the demonstration database `<name>_demo` (see [Demonstration database](#demonstration-database-d-140)); any other environment refuses to start with it |
| `Analytics:MinimumGroupSize` | `10` | Smallest demographic cell the analytics overview shows (approved, D-58) |
| `Exports:LocalDirectory` | none | Absolute folder for research export files, used only in Development and tests; when unset (or in any other environment), export requests return 409 |
| `Exports:MaxRows` | `50000` | Rows above which an export fails as too large |
| `Exports:RetentionDays` | `7` | Days an export file can be downloaded before maintenance deletes it (proposed, D-60) |
| `Exports:StalledAfterHours` | `24` | Hours after which maintenance fails a queued or running export and deletes its file |
| `Exports:MaxActivePerRequester` | `2` | Queued or running exports one researcher may have (1 to 10, D-80) |
| `Exports:MaxPerRequesterPerDay` | `20` | Exports one researcher may request in 24 hours (1 to 200, D-80) |
| `Authentication:ClockSkewSeconds` | `60` | Clock difference tolerated in token lifetimes (0 to 300, D-76) |
| `RateLimiting:Enabled` | `true` | Rate limits (D-79); only `false` turns them off (the shared test hosts do) |
| `DemoTools:Enabled` | `false` (`true` in `appsettings.Development.json`) | The demonstration tools: one tap answers, submits and scores a session for a low, medium or high result (`POST /api/v1/sessions/{id}/demo-submission`, D-143; the plan alone at `GET …/demo-answers`, D-130), and a new check-in round for the caller's household (`POST /api/v1/assignments/demo-check-in`, D-144); a deployed API refuses to start with it on |
| `RateLimiting:Global:SignedInPermitLimit`, `:AnonymousPermitLimit`, `:WindowSeconds` | `300`, `60`, `60` | The limit every request passes, per subject or per address |
| `RateLimiting:Policies:<name>:PermitLimit`, `:WindowSeconds` | see D-79 | `participant-writes`, `answers`, `identity`, `staff-reads`, `staff-writes`, `exports`, `export-downloads`, `data-exports`, `support-searches` (30 a minute, D-93) |
| `ReverseProxy:KnownProxies`, `ReverseProxy:KnownNetworks` | none | IPs or CIDR ranges whose `X-Forwarded-For`/`-Proto` are trusted (D-77) |
| `SupportResources:LanguageModel:Enabled` | `false` | The dormant support-resource language model (D-94). Only `true` switches it on, and then the API refuses to start unless every value below is valid and an `ILanguageModelClient` is registered. None exists, so leave it `false` |
| `SupportResources:LanguageModel:ApprovalReference` | empty | The decision record that approved the use (1 to 100 characters when enabled) |
| `SupportResources:LanguageModel:TimeoutSeconds` | `5` | Per model attempt (1 to 30 when enabled) |
| `SupportResources:LanguageModel:MaxAttempts` | `2` | Model attempts in total; only timeouts and transient faults are retried (1 to 3 when enabled) |
| `SupportResources:LanguageModel:MaxCandidates` | `12` | Directory entries offered to the model (1 to 20 when enabled) |
| `SupportResources:LanguageModel:MaxOutputTokens` | `256` | Output token limit passed to the provider (64 to 1024 when enabled) |

**Deployed environments** (anything but Development and Testing) refuse to start without an `https`
`Authentication:Schemes:Bearer:Authority` and an audience, with `SigningKeys`, with `RequireHttpsMetadata=false`, with a
wildcard or plain-HTTP CORS origin, with a connection string whose `SSL Mode` is not Require, VerifyCA or VerifyFull,
with `ASPNETCORE_FORWARDEDHEADERS_ENABLED` or a `ReverseProxy:KnownNetworks` range wider than /8 (IPv6: /32), or with
`RateLimiting:Enabled=false` (D-76), with `Database:ApplyMigrationsOnStartup=true` (D-86), or with
`Database:UseDemonstrationDatabase=true` or a `Default` or `Migrations` connection string whose database name ends in
`_demo`, in any letter case (D-140; a name that only contains `demo` elsewhere is fine). They accept HTTPS only
(D-77) and refuse tokens of the development identity provider, that is, tokens that carry the development marker
`azwah_development_only` (the identity provider issues them only in Development, D-129).

**Every environment** refuses to start with `SupportResources:LanguageModel:Enabled=true` unless an approval reference
is set, an `ILanguageModelClient` is registered and the other values are in range; the error lists problem codes only
(`LanguageModelStartupGuard`). No provider adapter ships, so no AI assistant is available; the directory search is
deterministic (D-93, D-94, [`docs/support-resource-discovery.md`](../docs/support-resource-discovery.md)).

The Azwah assistant, the live consultant and the written score report use OpenAI when the API has a key (D-112,
D-119, D-122 to D-126), and say they are not switched on otherwise. Put the key in `OPENAI_API_KEY` in the repository
root's git-ignored `.env` and restart the API. Compose passes it on, and in Development `dotnet run` reads that one
variable from the nearest `.env` above the project (`LocalEnvFile`), also from a worktree inside the checkout; a key in
user secrets (`Assistant:Model:ApiKey`) wins over it. Keep it out of commits and chats. Models: `Assistant:Model:ChatName`
(default `gpt-6.1-terra`, the chat agent and the memory), `:ChatReasoningEffort` (`low`), `:VoiceName` (`gpt-live-1`),
`:Voice` (`marin`), `:DelegateName` (the model a call delegates to; defaults to the chat model),
`:DelegateReasoningEffort` (`low`), `:ModerationName` (`omni-moderation-latest`), `:ReportName` (the score report,
default `gpt-5.4-mini`, D-116), and `:CrisisName` with `:CrisisReasoningEffort` (the consultant's supportive reply
when OpenAI's filter stops a call, default `gpt-5.4-mini` with `none`, D-141). A deployed API also needs `Assistant:Model:ApprovalReference`, naming the decision that
approves sending family data and answers to OpenAI (open questions 48 and 52), or it refuses to start. The report only
exists for a scored session, so the instrument version and its scoring rule set must be approved first (the
demonstration database has both, D-140).

The live evaluation (D-127) runs the real models against synthetic families, chats and synthetic speech, and writes a
Markdown report with pass rates and latency; it needs the key and costs money, so run it only when changing prompts,
models or the agent: `dotnet run --project FamilyResilience.Evaluation -- --suite all --out <report.md>` (`--suite
chat` or `voice`, `--only <part of a scenario name>`, `--min-pass 0.85`). See `FamilyResilience.Evaluation/README.md`.

The dispatcher's only handler writes research export files (D-60); every other event stays Pending. It never calls
anything outside the API. To try exports locally, point `Exports:LocalDirectory` at a folder outside the repository
with `dotnet user-secrets set`.

## Tests

    cd backend
    dotnet test --solution FamilyResilience.slnx

Docker must be running: the integration tests start PostgreSQL 18.6 with Testcontainers.

The support-resource evaluation (D-95) runs with the integration tests. To see its tables (synthetic set, aggregates
only), from `backend/`:

    dotnet test --project FamilyResilience.IntegrationTests --output detailed -- --filter-namespace "*SupportResources.Evaluation" --show-stdout All

## Database

From the repository root, with Docker running and `.env` in place:

    npm run db:up         # PostgreSQL only
    npm run db:migrate    # dotnet run -- db migrate
    npm run db:seed       # dotnet run -- db seed: synthetic data, Development only, idempotent
    npm run db:reset      # deletes the local database volume and starts PostgreSQL again

`db:migrate` and `db:seed` use the `ConnectionStrings:Default` from your user secrets (see above), or from the
`ConnectionStrings__Default` environment variable, which wins.

Persistence tests only: `npm run api:test:persistence`.

### Database roles (D-86)

Deployed databases use two roles. Once, as a database administrator:

    psql "<admin connection>" -f FamilyResilience.Infrastructure/Persistence/Sql/database-roles.sql

then set both passwords from the secret store (`ALTER ROLE azwah_migrator PASSWORD …`, `ALTER ROLE azwah_app
PASSWORD …`). Run the migration bundle as `azwah_migrator`, then, after every bundle:

    psql "<azwah_migrator connection>" -f FamilyResilience.Infrastructure/Persistence/Sql/application-role.sql

The API's `ConnectionStrings:Default` uses `azwah_app`. Locally one owner connection is fine.

### Erasing a household (D-88)

For an approved erasure request, with `Erasure:ConsentRecords=delete` configured:

    dotnet run -- db erase-household --household <household id> --reference <household reference> [--withdraw]

It refuses an active household unless `--withdraw` is given (withdrawing is audited), and a reference that does not
match. It deletes everything of the household in one transaction, writes one `households.household.erased` audit
event, and prints the number of rows deleted per table, never data. The household's open privacy requests are recorded
as closed in the audit trail (`privacy.request.closed`) and deleted with it, so there is nothing left to close in the
dashboard. Two erasures of one household cannot both run: the second waits for the first, then answers not found.

## Demonstration database (D-140)

A separate local database for a live, repeatable demonstration (phone → API → dashboard), filled with synthetic data.
**It is labelled LOCAL DEMONSTRATION ONLY: nothing in it is approved by a research owner.** Development only.

    npm run demo:db       # dotnet run -- db demo: build the demonstration database unless it is built (idempotent)
    npm run demo:reset    # dotnet run -- db demo --reset: wipe it and build it again (never a database it did not make)
    npm run demo:api      # dotnet run -- --Database:UseDemonstrationDatabase=true: the API against it (port 5080)

**The guard.** The demonstration database is always derived from your configured `ConnectionStrings:Default` (and
`Migrations`, when set, which must name the same database on the same host and port, or the command refuses): the same
server and credentials, with the database `<name>_demo` (`azwah` → `azwah_demo`). A configured name that already ends
in `_demo` is kept, so your configured database is then itself the target; it gets no exemption and is checked like any
other. Otherwise your ordinary database is never written to.

**Nothing is dropped or migrated unseen.** Before `db demo` drops, migrates or seeds the target, it inspects it, and goes
on only when the database is absent, empty (no row in any table of any schema, EF Core's migration history aside: one
created by hand, or migrated and never seeded) or carries the marker a completed build writes (the audit event
`demonstration.database.seeded` by `azwah-demonstration-seed`; the audit table exists from the first migration, so a
demonstration built on an older migration is recognised too). Any other database, such as an `azwah_demo` you made
yourself on another branch, is refused with "The database '<name>' was not made by 'db demo': it holds data and no
demonstration marker. It was left untouched …": nothing is dropped, migrated or written. To delete such a database and
build the demonstration in its place, run `dotnet run -- db demo --reset --replace-foreign-database` yourself: that
option is never the default, is accepted only with `--reset`, and no npm script passes it.

`db demo` creates the database when it is absent, migrates it as the schema owner and seeds it through services of its
own. The seeder itself also refuses any database whose name does not end in `_demo`, and one that holds anything but
the demonstration. `db demo` and `Database:UseDemonstrationDatabase` are refused in every environment but Development,
and the deployed-settings guard refuses both the setting and a `Default` or `Migrations` connection string whose database
name ends in `_demo` (`demonstration_database`). Everything is written in one transaction, so a build either completes
or leaves the database empty; running `db demo` again changes nothing. Audit events are append-only and approved content
is immutable, so `--reset` drops the whole `_demo` database (`DROP DATABASE … WITH (FORCE)`, which also ends the
connections of a running `demo:api`: restart it) and rebuilds it. It drops only a database that is empty or carries the
marker, whatever migration it is on, or one you name for replacement with `--replace-foreign-database`. A build takes
about 15 seconds. Dates are relative to the day it is built and wave two closes ten weeks later, so run `demo:reset`
shortly before a demonstration. If the API uses its own role (D-86), run `application-role.sql` against `azwah_demo` too.

**Approved in it, locally only**, each with the approver `azwah-demonstration-seed` (a subject no identity provider
issues) and the source `LOCAL DEMONSTRATION ONLY (D-140): not approved by a research owner`, so anyone reading the
database or its audit trail sees what they are:

- the household assessment, version 1, from the content file the development seed imports (no content file changes);
- its proposed scoring rule set `household_assessment_total` v1 (the brief's bands), run by the platform's engine;
- the eligibility rule set `demo_household_assessment_head` v1: the head of household answers the household assessment;
- one consent policy per purpose, with the document reference `demo-placeholder (not reviewed consent text)`.

**Not approved**, because nothing approvable exists: the Abu Dhabi Family Survey stays a draft (its Arabic is
incomplete, so the domain and the database refuse approval, and it has no scoring proposal) and its three eligibility
rule sets stay proposed. There is no referral rule set, partner organisation or support resource, so the app and the
dashboard show their empty states. The real platform still approves nothing until a named approver acts (D-99).

**What it holds** (fixed seed; no names or contact details): 60 households (58 active, 2 withdrawn; 59 with a confirmed
roster), 205 members. Wave `demo_wave_1` opened about seven months ago and closed about four months ago: 55
assignments, 47 completed and scored, 8 expired (2 of them abandoned part-way). Wave `demo_wave_2` opened about three
weeks ago and closes in about ten weeks: 58 assignments, 40 completed and scored, 8 in progress with some answers, 9 not
started and 1 cancelled (that household's head changed). Every session was answered through the domain and scored by
the approved rule set; each wave has at least ten research-consented people in each band (`vulnerable`, `transitional`,
`flourishing`), so the distributions are shown, and heads fall in three age bands and three employment statuses with
at least ten people each. Four households gave no research-use consent. Wave one's expiries and abandonments and the
two withdrawals carry the audit events the platform writes for them (`assignments.assignment.expired`,
`sessions.session.abandoned`, `households.household.withdrawn`), recorded as `azwah-demonstration-seed`.
`DemonstrationDataTests` checks the household, member, assignment, session and result counts above against a fresh build.

**Personas** of the identity provider (`npm run identity:dev`) linked to it:

| Persona | In the demonstration |
| --- | --- |
| `participant_lead` | Head and manager of household `HH-DM01-…`: wave one scored, wave two assigned and not started, so the presenter answers it live, sees the score and the dashboard's counts change |
| `participant_member` | The same household's spouse, with an account of their own: no questionnaire is assigned to them and the head's score is not given to them (D-103) |
| `participant_new` | No household: enrol in the app with yourself as head, confirm the roster and the wave-two household assessment is assigned |
| Staff personas | Nothing seeded: roles come from the token (researcher: analytics and score distributions; programme administrator: overview and monitoring; system administrator: household results; support coordinator: referrals, empty) |

**Point the clients at it.** The API (`demo:api`) must trust the identity provider (the user secrets in
[Identity provider](#identity-provider-uae-pass-staging-simulated-personas-in-development)).

- Admin dashboard, `apps/admin/.env.local`: `ADMIN_ENVIRONMENT=development`, `ADMIN_AUTH_MODE=oidc`,
  `OIDC_ISSUER=http://localhost:5090`, `OIDC_CLIENT_ID=azwah-admin`, `OIDC_CLIENT_SECRET=<the value of
  IdentityProvider:Clients:azwah-admin:Secret>`, `ADMIN_SESSION_SECRET=<base64 of 32 random bytes>`,
  `ADMIN_DATA_SOURCE=api` and `ADMIN_API_URL=http://localhost:5080`; sign in with a staff persona.
- Phone, `apps/mobile/.env`: `EXPO_PUBLIC_DEV_FIXTURES=false`, `EXPO_PUBLIC_OIDC_ISSUER=http://localhost:5090`,
  `EXPO_PUBLIC_OIDC_CLIENT_ID=azwah-mobile` and `EXPO_PUBLIC_API_URL=http://10.0.2.2:5080` (Android emulator), or
  `adb reverse tcp:5090 tcp:5090` and `adb reverse tcp:5080 tcp:5080` with `http://localhost:5080`; sign in with a
  participant persona.
- Research exports need `Exports:LocalDirectory` set in Development (`dotnet user-secrets set "Exports:LocalDirectory"
  "<an absolute folder outside the repository>"`); without it, export requests return 409.

## Migrations

    cd backend
    dotnet tool restore
    dotnet tool run dotnet-ef migrations add <Name> \
      --project FamilyResilience.Infrastructure --startup-project FamilyResilience.Infrastructure \
      --output-dir Persistence/Migrations

Never edit a merged migration. `AddDatabaseGuards` is hand-written SQL (triggers, exclusion constraint); keep any
future hand-written SQL in its own migration too. Rules: [`docs/database-conventions.md`](../docs/database-conventions.md).

Production never migrates on startup. Build a bundle and run it before the new version starts:

    dotnet tool run dotnet-ef migrations bundle --project FamilyResilience.Infrastructure \
      --startup-project FamilyResilience.Infrastructure --self-contained -o efbundle
    ./efbundle --connection "<connection string>"
