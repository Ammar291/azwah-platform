# Contributing to Azwah

The rules, conventions and commands for everyone who changes this code. Code comments and the documents in `docs/`
cite the numbered security rules below as "CONTRIBUTING.md rule N". **Treat every participant answer as sensitive
personal information.**

## Repository map

| Path | What it is |
| --- | --- |
| `apps/mobile` | iOS and Android app (Expo) |
| `apps/admin` | Researcher and administrator dashboard (Next.js) |
| `backend` | API, modular monolith (`FamilyResilience.*`); `FamilyResilience.IdentityProvider` is the platform's OpenID Connect provider (UAE PASS staging; Google when configured, D-147; simulated personas in Development only), never referenced by the API; `FamilyResilience.Evaluation` is the live evaluation of the AI helpers (D-127), run only when asked |
| `infra` | Docker Compose for the local stack (PostgreSQL, API, identity provider, admin dashboard) and the monitoring alert rules |
| `docs` | Architecture, domain model, conventions, security model, threat model, retention policy (draft), assessment rules, decisions, plan, demonstration guide, the support knowledge graph |

## Stack (exact versions in use)

| Area | Versions |
| --- | --- |
| Backend | .NET SDK 10.0 (global.json: 10.0.100, latestFeature), ASP.NET Core 10, EF Core 10.0.12, Npgsql EF 10.0.3, EFCore.NamingConventions 10.0.1, FluentValidation 12.2.0, OpenTelemetry 1.19, xUnit v3 4.0.2 on Microsoft Testing Platform, Testcontainers.PostgreSql 4.16.0 |
| Database | PostgreSQL 18.6 |
| Mobile | Expo SDK 57, React Native 0.86.3, React 19.2.3, Expo Router, TypeScript strict, i18next, TanStack Query 5, Zod 4, React Hook Form 7, Expo SQLite, expo-crypto (AES-GCM) and expo-secure-store, expo-web-browser, expo-network, expo-print, react-native-svg, Jest (jest-expo) + React Native Testing Library 14 |
| Admin | Next.js 16.4.6, React 19.3.0, Tailwind CSS 4, shadcn/ui, TanStack Query 5, Recharts 3, Zod 4, openid-client 6, jose 6, sonner 2, Vitest 5, user-event 14 |
| Tooling | Node 24, npm, Docker Compose, GitHub Actions |

Each JavaScript app is its own npm project with its own lockfile (Expo and Next.js pin different React versions).
Use `npx expo install` for native mobile packages. Never add prerelease packages. Backend package versions live only
in `backend/Directory.Packages.props`.

## Architecture rules

- Projects: `Domain` (no references) ← `Application` (Domain only) ← `Infrastructure` (Application, Domain);
  `Api` references Application and Infrastructure. `DependencyRulesTests` fails the test run otherwise.
- `Application` never references EF Core, ASP.NET Core or Npgsql. It talks to storage through `IUnitOfWork` and
  repository interfaces.
- Modules are folders in each layer: Identity, Households, Members, Consent, Questionnaires, Assignments, Sessions,
  Scoring, Referrals, Analytics, Privacy, SupportResources, Safeguarding, Audit. Each
  `backend/FamilyResilience.Application/<Module>/README.md` states what it owns and what others may use. Cross-module
  calls go through Application interfaces, never another module's tables.
- Controllers translate HTTP to handlers and back. No business rules in controllers, filters or clients.
- Scoring and eligibility exist only in the backend Scoring and Assignments modules: deterministic, versioned, and
  only from approved rule sets. Clients display results; they never compute them.
- Expected failures return `Result`/`Error`; exceptions are for bugs and infrastructure faults.
- The admin dashboard's browser code calls only its own BFF (`/bff/...`). A new API call needs a route in
  `apps/admin/src/lib/server/bff-routes.ts`, a Zod schema in `src/lib/api/schemas.ts` and, for mock mode, a route in
  `src/lib/mock/backend.ts` (`src/lib/mock/contract.test.ts` checks the mock against the schemas).

Domain terms are defined in [`docs/domain-model.md`](docs/domain-model.md).

## Naming

- C#: namespaces `FamilyResilience.<Layer>.<Module>`; one public type per file; `…Request`/`…Response` records for
  HTTP; `…Validator` next to its request; async methods end in `Async`; tests are sentences in snake case
  (`Ready_returns_503_unhealthy_without_revealing_why`).
- Database: snake_case plural tables (`audit_events`), snake_case columns, `*_at` timestamps as `timestamptz` in UTC,
  `uuid` keys (version 7), enums stored as text.
- HTTP: `/api/v1/<plural-noun>`, JSON in camelCase.
- Mobile: components `PascalCase.tsx`, other modules `camelCase.ts`, routes under `apps/mobile/app/`, code under
  `apps/mobile/src/`, import alias `@/`. Translation keys are dotted camelCase; status values are snake_case.
- Admin: follow create-next-app and shadcn file naming (kebab-case files).

## Security rules (non-negotiable)

1. No scoring or eligibility logic in any client.
2. Never trust a household ID, member ID, role or permission sent by a client; authorise on the server from the
   validated token and the platform's own records.
3. Never log credentials, tokens, `Authorization` headers, request or response bodies, or query strings.
4. Never commit secrets. Local secrets go in `.env` (ignored) or `dotnet user-secrets`.
5. Never return exception messages or stack traces to clients (the global exception handler returns a generic
   ProblemDetails with a trace ID).
6. Never hard-code identity-provider credentials.
7. Never use AI to score or decide eligibility.
8. Never claim an integration exists that is not configured. **Production UAE PASS is not onboarded**; the Azwah
   identity provider signs in with UAE PASS staging and its public sandbox client (D-96, D-129), and with Google only
   when it has a Google OAuth client (D-147). OpenAI answers the assistant and the consultant and writes the score
   report only when the API has a key, and a deployed API also needs an approval reference (D-112, D-119).
9. Never invent questionnaire content, scoring thresholds or research rules. Unapproved content stays marked as such.
10. Validate all externally supplied data (FluentValidation on the server, Zod on clients).
11. Every schema change is an EF Core migration.
12. All meaningful business logic has automated tests.
13. Record every important decision in `docs/decisions.md`.
14. No microservices, Kubernetes, Redis, Kafka or vector databases without a documented, concrete requirement.
15. No synthetic participant data in production.
16. Participant answers are sensitive personal information everywhere: storage, logs, analytics, screenshots.
17. A new endpoint that receives participant free text screens it with `CrisisSignalScreen`, records with
    `ISafetySignalRecorder` (`CancellationToken.None`) and returns `safetyNotice` (D-135, D-138).

Every endpoint requires a signed-in user unless it is explicitly `[AllowAnonymous]`, and names an authorization policy
unless `DenyByDefaultTests` lists it as an exception. Every state-changing endpoint names a rate-limit policy
(`[EnableRateLimiting]`, D-79). Browser origins must be listed in `Cors:AllowedOrigins`. Deployed environments (anything
but Development and Testing) refuse unsafe settings at start-up and accept HTTPS only (D-76, D-77). Details:
[`docs/security-model.md`](docs/security-model.md), [`docs/threat-model.md`](docs/threat-model.md).

## API and database conventions

Versioned routes `/api/v1/…`; ProblemDetails (RFC 9457) for every error; 400 `ValidationProblemDetails` with camelCase
keys; ISO 8601 UTC timestamps; UUID identifiers; OpenAPI at `/openapi/v1.json` in Development only. Full rules:
[`docs/api-conventions.md`](docs/api-conventions.md).

PostgreSQL 18.6 through EF Core; configurations in `Infrastructure/Persistence/Configurations`; migrations in
`Infrastructure/Persistence/Migrations`, created with `dotnet tool run dotnet-ef migrations add <Name> --project
FamilyResilience.Infrastructure --startup-project FamilyResilience.Infrastructure --output-dir Persistence/Migrations`.
Never edit a migration that has been merged. Migrations run on startup only in development; production applies a
migration bundle first. `PersistenceModelTests.Model_has_no_changes_missing_from_the_migrations` fails if you forget.
Hand-written SQL lives in its own migration (`AddDatabaseGuards`), never in a generated one. Full rules:
[`docs/database-conventions.md`](docs/database-conventions.md).

## Testing

- Write the failing test first. Business logic needs unit tests; every endpoint needs integration tests covering
  success, validation failure, 401, 403 and not-found where they apply.
- Backend integration tests use a real PostgreSQL (Testcontainers); never an in-memory database substitute.
- Pass `TestContext.Current.CancellationToken` to everything that accepts a token in xUnit tests (analyzer xUnit1051
  is an error here).
- Mobile: Jest + React Native Testing Library (`await render(...)`, `userEvent`); test helpers live outside
  `__tests__` folders. Run the mobile suite on its own: next to other heavy jobs it can time out spuriously.
- Admin: Vitest + Testing Library.
- A change is done only when its component's full check list below passes.

## Mobile UI and right to left

- Use the design system in `apps/mobile/src/design-system` (`apps/mobile/README.md`). Pine is the working colour; gold
  and sand are brand furniture only; status colours are reserved for status.
- Touch targets at least 44×44 points. Every interactive element has an accessibility role and label.
- All user-visible text comes from `src/i18n/resources/{en,ar}.json`; add every key to both files in the same change
  (a test enforces matching keys and Arabic script). Show server problems as translated states; never show
  server-provided text. Answer options come from an approved instrument version, never hard-coded.
- Arabic lays out right to left. Use `start`/`end` styles only (`marginStart`, `paddingEnd`, `start`, `end`,
  `borderStartWidth`); `left`/`right` and `textAlign: 'left'|'right'` are lint errors. Mirror directional icons.
  Western digits in both languages.
- Admin: `lang` and `dir` on `<html>`; use logical Tailwind utilities (`ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`,
  `text-start`).

## Commands

| Component | Run | Check (all must pass) |
| --- | --- | --- |
| Whole stack | `cp .env.example .env`, fill the secrets, then `npm run infra:up` / `npm run infra:down` (README.md, "Quickstart") | `curl http://localhost:8080/health/ready` → `{"status":"Healthy"}` |
| Identity provider | `npm run identity:dev` (port 5090; see `backend/FamilyResilience.IdentityProvider/README.md`) | Covered by the backend check (`IntegrationTests/IdentityProvider`) |
| Backend | `cd backend/FamilyResilience.Api && dotnet run` (port 5080; see `backend/README.md` for user secrets) | `cd backend && dotnet build FamilyResilience.slnx && dotnet test --solution FamilyResilience.slnx` (Docker running) |
| Database | `npm run db:up`, `npm run db:migrate`, `npm run db:seed` (Development only), `npm run db:reset` | `npm run api:test:persistence` (Docker running) |
| Mobile | `cd apps/mobile && npm start` (Expo Go); `npm run android:dev` for a development build | `npm run typecheck && npm run lint && npm test && npx expo export --platform android --platform ios && npm run doctor` |
| Admin | `cd apps/admin && cp .env.example .env.local && npm run dev` (port 3000; mock data and development sign-in by default) | `npm run typecheck && npm run lint && npm test && npm run build` |
| Demonstration database | `npm run demo:db`, `npm run demo:reset`, `npm run demo:api` (Development only; always the `<name>_demo` database, D-140; see `docs/demo-guide.md`) | Covered by the backend check (`DemonstrationDataTests`, `DemonstrationPersonaTests`, `DemonstrationResetTests`) |

## Working on a change

- Keep a change to one module or one app where you can. Shared hot spots need care:
  `backend/FamilyResilience.Api/Program.cs`, `backend/Directory.Packages.props`, `FamilyResilienceDbContext`, the
  migrations folder, and the mobile translation files.
- Only one change at a time adds a migration. If two branches both add one, the later branch deletes its migration,
  rebases and generates it again.

## Prohibited shortcuts

- Disabling tests, analyzers, `TreatWarningsAsErrors`, TypeScript strictness or lint rules to get green.
- `// @ts-ignore`, `any` casts or `!` to silence a real type error; `#pragma warning disable` without a written reason.
- Mocking the database in backend integration tests.
- Calling the API from a client without Zod validation of the response.
- Putting secrets, real participant data or screenshots of participant data into the repository, logs or tickets.
- Activating unapproved questionnaire content or scoring rules, even "temporarily".
- Pretending an external integration works (identity verification, UAE PASS, partner referrals) when it is simulated or
  absent. Simulated behaviour is labelled in the docs, and in the UI everywhere except the phone app, whose labels were
  removed for a recorded demonstration (D-142): never add a false claim in their place, and restore them, with the
  OpenAI disclosure, before any real family uses the app (open question 69, T-35).

## Known pitfalls

- **Expo Go cannot show right-to-left layout** on SDK 57: it resets the layout direction for every project. Arabic text
  appears, but the layout stays left to right. Check RTL in a development build (`npx expo run:android`).
- **Expo Go ignores `reloadAppAsync`**: never treat a requested reload as certain; `applyDirection` asks for none in
  Expo Go, and the language switch unlocks if the reload never comes.
- **Windows path length.** The Android native build fails with "Filename longer than 260 characters" from a deep folder.
  Build from a short clone path (for example `C:\src\azwah`). A `subst` drive or junction does not work.
- **Ports.** Defaults are PostgreSQL 5432, API 8080, identity provider 5090 and dashboard 3000. If another project holds
  them, override `POSTGRES_PORT`, `API_PORT`, `IDENTITY_PROVIDER_PORT` (with `IDENTITY_PROVIDER_ISSUER`) and
  `ADMIN_PORT` in your local `.env` (and the mobile API URLs). Metro defaults to 8081: use `--port 8088` if taken.
- **`dotnet run --environment`** sets an environment variable in .NET 10, not the hosting environment. Use plain
  `dotnet run` (the launch profile sets Development) so user secrets load.
- **TypeScript 6** no longer includes `@types/*` automatically: the mobile `tsconfig.json` lists `"types": ["jest"]`.
- **No `supportsRTL`/`forcesRTL` on the expo-localization plugin.** On iOS they make the module force the device's
  direction on every launch, undoing the language chosen in the app (`projectConfig.test.ts` guards this).
- **`expo prebuild` / `expo run` rewrite the `android` and `ios` scripts** in `package.json` to `expo run:*`. Keep
  `android`/`ios` on `expo start` (Expo Go) and use `android:dev`/`ios:dev` for native builds.
- **TanStack Query never goes offline on React Native by itself.** `bindOnlineManager` (`src/network`) feeds it
  expo-network; queries then pause offline instead of failing, so screens need an offline state, and reads of local data
  need `networkMode: 'always'`.
- **Next.js 16 calls middleware `proxy.ts`, and it is its own bundle.** It shares no module state with route handlers:
  derive per-process values (for example a session key) from configuration.
- **Admin route types are generated.** After deleting or renaming an admin route, run `npm run build` before
  `npm run typecheck`, or `tsc` reports the stale `.next/types`.
- **react-native-svg has no layout direction.** Measure chart positions from the start edge and mirror them for
  right to left yourself (`toSvgX` in `apps/mobile/src/features/resilience/trendGeometry.ts`).
- **Recharts draws nothing in jsdom** (no layout size). Test charts through `ChartFigure`'s summary and table view.
- **`renderRouter` (expo-router/testing-library) turns on Jest fake timers.** Read fixtures before rendering, and set up
  `userEvent` with `advanceTimers: jest.advanceTimersByTime`.
- **Answering and submitting are separate commits**, and so are scoring and referring: database triggers refuse
  response changes once a session is Submitted and a referral until the session is Scored. Submitting, scoring and
  routing are three commits inside one `ExecuteInTransactionAsync`, after `ISessionLock` (D-52).
- **PostgreSQL cuts identifiers at 63 characters.** Name long composite foreign keys with `HasConstraintName` and give
  their indexes explicit names.
- **Raw SQL against approved content fails by design** (SQLSTATE 23001). Change content by creating a new draft
  version.
- **Never name a paging parameter `page`**: MVC treats the `page` query key as the model's prefix. Name it `request`.
- **Participant endpoints answer 404 for another household's resources**; only a missing role (or missing consent) is
  403.
- **A new table that references a household or member must join the erasure** (`HouseholdErasure`, in foreign-key
  order) with a case in `HouseholdErasureTests`.
- **The API runs as `azwah_app` in production**, which has DML only and no UPDATE or DELETE on `audit_events` (D-86).
- **The identity provider's issuer must be the same URL for every client.** For an Android emulator or a USB phone use
  `adb reverse tcp:5090 tcp:5090` (and the API port), or opt in to LAN listening (`IDENTITY_PROVIDER_BIND=0.0.0.0`) and
  set the issuer to the computer's LAN address everywhere. In Compose, the dashboard's container reaches a localhost
  issuer through the `issuer-forward` sidecar (`infra/docker-compose.yml`).
- **OpenAI is connected only where the API has a key** (`OPENAI_API_KEY` in `.env`, or `Assistant:Model:ApiKey`). The
  key never goes in the app or a commit. Tests never call OpenAI; they use fakes. A deployed API refuses a key without
  `Assistant:Model:ApprovalReference`.
- **.NET does not race IPv4 and IPv6.** Where IPv6 is advertised but broken, each IPv6 address of an outside service
  hangs until it times out. Outbound connections go through `HappyEyeballs.CreateHandler()` (D-121).
- **Never call `ISafetySignalRecorder` inside a transaction that locks or creates the subject's rows.** It saves in its
  own scope and would wait on that lock.
- **Run the live evaluation after changing a prompt, model, tool or the agent** (`backend/FamilyResilience.Evaluation`,
  D-127). Unit tests use scripted models and cannot see how the real ones behave.
- **The live consultant needs the native `AzwahVoice` module for echo cancellation** (`apps/mobile/modules/azwah-voice`,
  D-125). Expo Go and Jest fall back to expo-audio (half duplex); never judge voice quality there.
