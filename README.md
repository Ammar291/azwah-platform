# Azwah (عزوة) family resilience platform

Azwah ("the family and kin who stand by you") is a bilingual Arabic–English platform for assessing and supporting family
resilience over time. Households answer versioned questionnaires on a phone app; a deterministic engine on the server
decides who answers what and computes the scores; support referrals happen only with consent; researchers and
administrators see de-identified results on a web dashboard.

**Status: built, with the research content awaiting approval.** Sign-in, households and members, consent,
questionnaires, eligibility and assignment, answering on the phone, deterministic scoring, support routing, programme
monitoring, de-identified analytics, research exports, an AI assistant and voice consultant, a written score report with
the family's next steps, and safety alerts to the dashboard are built and tested
([`docs/development-plan.md`](docs/development-plan.md)). **No questionnaire version or scoring rule set has been
approved by a research owner**, so a real environment assigns and scores nothing until a named approver acts
([`docs/assessment-rules.md`](docs/assessment-rules.md)). Everything below runs on synthetic data.

> **Never use this with real families.** It is not production software: the sign-in you get locally checks no
> identity, the demonstration data is made up, and the phone app has its demonstration labels removed (see
> [What is simulated or not configured](#what-is-simulated-or-not-configured)).

## What is in this repository

| Path | What it is |
| --- | --- |
| `apps/mobile` | iOS and Android app (Expo SDK 57, React Native, Expo Router) |
| `apps/admin` | Researcher and administrator dashboard (Next.js 16) |
| `backend` | API: .NET 10 modular monolith (`FamilyResilience.*`), and the Azwah identity provider (OpenID Connect) |
| `infra` | Docker Compose for the local stack (PostgreSQL 18.6, API, identity provider, dashboard) and monitoring alert rules |
| `docs` | Architecture, domain model, conventions, security and threat models, assessment rules, decisions, plan, demonstration guide |

## Prerequisites

- **Docker** (Docker Desktop on Windows and macOS) with Docker Compose v2: enough to run the API, the identity
  provider, PostgreSQL and the dashboard.
- **Node.js 24** and npm: for the phone app, and to run the dashboard outside Docker.
- **.NET SDK 10**: only to run the backend or its tests outside Docker.
- For the phone: the **Expo Go** app on an Android phone or iPhone (quickest), or **Android Studio** with an emulator
  for a development build.

## Quickstart

Run every command from the repository root. Default ports: dashboard 3000, identity provider 5090, API 8080,
PostgreSQL 5432 (change `ADMIN_PORT`, `IDENTITY_PROVIDER_PORT` with `IDENTITY_PROVIDER_ISSUER`, `API_PORT` and
`POSTGRES_PORT` in `.env` if they are taken).

**1. Configure.**

```bash
git clone <this repository> azwah && cd azwah
cp .env.example .env
```

Fill in three secrets in `.env` with random values, for example each from
`node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"` (or `openssl rand -base64 32`):
`POSTGRES_PASSWORD`, `IDENTITY_PROVIDER_ADMIN_CLIENT_SECRET` and `ADMIN_SESSION_SECRET`.

**2. Start the stack.**

```bash
docker compose --env-file .env -f infra/docker-compose.yml up --build -d
curl http://localhost:8080/health/ready        # {"status":"Healthy"} once the API has started
```

This runs PostgreSQL, the API (Development, migrations applied on start), the identity provider (Development, with test
personas) and the dashboard. The first build takes a few minutes. The database is still empty.

**3. Build the demonstration data** (synthetic: 60 households, two waves, scored results; D-140).

```bash
docker compose --env-file .env -f infra/docker-compose.yml run --rm api db demo
```

Then set `USE_DEMONSTRATION_DATABASE=true` in `.env` and point the API at it:

```bash
docker compose --env-file .env -f infra/docker-compose.yml up -d
```

The demonstration database (`azwah_demo`) approves the household assessment and its scoring **locally only**, so that
something can be assigned and scored; the ordinary database stays as a real environment would be, with nothing approved.
Rebuild it from scratch with `docker compose --env-file .env -f infra/docker-compose.yml run --rm api db demo --reset`
(then `docker compose --env-file .env -f infra/docker-compose.yml restart api`).

**4. Open the dashboard.** Go to <http://localhost:3000>, choose **Sign in with your organisation account**, and pick a
staff persona on the identity provider's page: **System administrator** (household results, questionnaires and rules,
safety alerts), **Researcher** (overview, score distributions, exports), **Programme administrator** or **Support
coordinator**. No password is asked: these test personas check no identity.

**5. The phone app.**

- *Quickest, no server:* Expo Go in fixtures mode, with simulated sign-in and made-up data inside the app.

  ```bash
  cd apps/mobile
  npm ci
  cp .env.example .env          # EXPO_PUBLIC_DEV_FIXTURES=true is the default
  npm start                     # scan the QR code with Expo Go
  ```

- *Full path, against the local stack:* a development build on an Android emulator or a USB phone. In
  `apps/mobile/.env` set `EXPO_PUBLIC_DEV_FIXTURES=false`, `EXPO_PUBLIC_API_URL=http://localhost:8080`,
  `EXPO_PUBLIC_OIDC_ISSUER=http://localhost:5090` and `EXPO_PUBLIC_OIDC_CLIENT_ID=azwah-mobile`, then:

  ```bash
  adb reverse tcp:8080 tcp:8080
  adb reverse tcp:5090 tcp:5090
  npm run android:dev           # builds and installs the development build (from a short path on Windows)
  ```

  Sign in with **Other sign-in options** and a participant persona; on the demonstration database
  **Participant who leads a household** has a questionnaire waiting. Details, Arabic right-to-left layout (which needs a
  development build, not Expo Go) and the Windows path-length pitfall: [`apps/mobile/README.md`](apps/mobile/README.md).
- *A release APK for other phones* needs the stack behind an HTTPS address: see "Shareable demonstration APK" in
  [`apps/mobile/README.md`](apps/mobile/README.md).

**6. Optional integrations.**

- **OpenAI** for the assistant (chat), the consultant (voice) and the written score report: put your key in
  `OPENAI_API_KEY` in `.env` and run `up -d` again. Without a key they say they are not switched on. OpenAI is not
  approved to receive family data (open questions 48 and 52), so use synthetic data only, and set a spending cap.
- **Google sign-in** (D-147; staff sign-in to the dashboard on a demonstration stack, D-149): create your own Google
  OAuth client and set `IDENTITY_PROVIDER_GOOGLE_CLIENT_ID` and `IDENTITY_PROVIDER_GOOGLE_CLIENT_SECRET` as `.env.example`
  and [`backend/FamilyResilience.IdentityProvider/README.md`](backend/FamilyResilience.IdentityProvider/README.md)
  explain.
- **UAE PASS staging**: set the public sandbox client (`IDENTITY_PROVIDER_UAEPASS_CLIENT_ID` and `…_SECRET`, both
  `sandbox_stage`) and sign in with a UAE PASS staging test account.

**Stop** with `docker compose --env-file .env -f infra/docker-compose.yml down` (add `-v` to delete the databases).

Other ways to run it (the API with `dotnet run`, the dashboard with mock data and no server, a presentation's order):
[`docs/demo-guide.md`](docs/demo-guide.md), [`backend/README.md`](backend/README.md),
[`apps/admin/README.md`](apps/admin/README.md).

## What is simulated or not configured

- **Test personas.** Locally, the Azwah identity provider lets anyone sign in as any test persona; it checks no
  identity. It offers personas only in Development and refuses to start deployed without real settings (D-129).
- **UAE PASS.** Only UAE PASS **staging** with its public sandbox client is supported (D-96, D-129). Production UAE PASS
  is not onboarded.
- **Approved content.** The 39-item household assessment and the Abu Dhabi Family Survey modules A, B and C are in the
  platform as drafts, with their scoring and eligibility rules as proposals. Nothing is assigned or scored in a real
  environment until a named research owner approves a version and its rule sets; only the local demonstration database
  approves anything. The survey's source documents (codebook and manual) are third-party and not included in this
  repository.
- **Synthetic data only.** Every household, answer and score in the demonstration database and the app's fixtures is
  made up. Never enter real participant data.
- **The phone app shows no demonstration labels** (removed for a recorded demonstration, D-142); they and the OpenAI
  disclosure must be restored before any real family uses it (open question 69). The dashboard labels simulated data on
  every page.
- **Partner referrals.** A support coordinator can record a manual hand-off to a registered partner; the platform sends a
  partner nothing (D-61).
- **AI helpers.** OpenAI is used only where the API has a key, never to score or decide eligibility; a deployed API
  refuses a key without an approval reference. The organisations in the report's next steps are researched from public
  sources and not reviewed by them (open question 50).
- **Demonstration tools.** The Low, Medium and High buttons that finish a questionnaire in one tap (D-130, D-143), New
  survey (D-144) and the demo family button (D-131) work only against a Development API or in development builds.

## Read next

- [`CONTRIBUTING.md`](CONTRIBUTING.md): rules, conventions, commands and checks for everyone who changes this code
- [`docs/architecture.md`](docs/architecture.md), [`docs/domain-model.md`](docs/domain-model.md),
  [`docs/api-conventions.md`](docs/api-conventions.md), [`docs/database-conventions.md`](docs/database-conventions.md)
- [`docs/security-model.md`](docs/security-model.md), [`docs/threat-model.md`](docs/threat-model.md),
  [`docs/retention-policy.md`](docs/retention-policy.md)
- [`docs/assessment-rules.md`](docs/assessment-rules.md), [`docs/scoring-specification.md`](docs/scoring-specification.md),
  [`docs/decisions.md`](docs/decisions.md) (decisions and open questions), [`docs/development-plan.md`](docs/development-plan.md)
- [`backend/README.md`](backend/README.md), [`apps/mobile/README.md`](apps/mobile/README.md),
  [`apps/admin/README.md`](apps/admin/README.md)

## Licence

No licence has been chosen yet: all rights reserved.
