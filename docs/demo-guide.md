# Demonstration guide

How to show Azwah to judges, researchers and partners, what is real, what is synthetic, and how to get back to a known
state. Two ways to run it:

| | A. Self-contained | B. Live stack |
| --- | --- | --- |
| What runs | The dashboard on mock data and the phone app on fixtures | PostgreSQL, the API, the identity provider, the dashboard and the phone app |
| Needs | Node only | Docker, .NET, Node, an Android emulator or phone |
| Shows | Every screen in both languages, the real questions, a simulated score | The same journey through the real API: a person answers on the phone and the dashboard's numbers change |
| Data | Synthetic; labelled on every dashboard screen, not on the phone (D-142) | Synthetic, in a separate `_demo` database (D-140) |
| Risk on stage | Lowest | More moving parts; rehearse it |

**Nothing shown in either is a research finding.** No questionnaire version or scoring rule set has been approved by a
research owner ([`assessment-rules.md`](assessment-rules.md)). Say so at the start.

## Before any demonstration

- Run it once from a cold start the day before, on the machine and network you will present on.
- Free memory: close other development servers. The first page of the dashboard fetches its fonts, so open it once
  while online.
- Present the dashboard at 1280 pixels wide or more; wide tables scroll sideways inside their cards on a phone.
- Arabic right-to-left layout on the phone needs a development build, not Expo Go
  ([`apps/mobile/README.md`](../apps/mobile/README.md)). The dashboard is right to left in any browser.
- A development build made before the score report, the trend chart and the voice module were merged lacks native
  code they use (`expo-print`, `react-native-svg`, `AzwahVoice`): rebuild it with `npm run android:dev`. Expo Go has
  the first two built in.
- A phone that once ran the unmerged score report branch has a local database the merged app cannot upgrade (its
  migrations 5 and 6 were renumbered 6 and 7): clear the app's storage or reinstall it.
- The phone app locks after two minutes in the background or ten minutes idle, and on every cold start. Set a screen
  lock on the device so "Unlock" is offered; without one the only way back is to sign in again.
- **The phone labels nothing (D-142).** At the owner's direction, for a recorded demonstration on synthetic data, the
  app no longer shows which parts are simulated, staged, not yet approved or sent to OpenAI: no development banner,
  no "(staging)" on UAE PASS, no "simulated" on the fixtures' sign-in, assistant or consultant, no OpenAI note before
  the score report. The table below is now the only place that says so: say it in the recording's narration or its
  description. The demo tools are quiet buttons with neutral names (**Low / Medium / High**, **New survey**,
  **Auto-fill**, Settings → **Reset data**, sign-in → **Other sign-in options**). This build must not be used with real
  families (open question 69).
- A phone that kept fixture data from an earlier build starts its simulated accounts again from their seeds once
  (the kept snapshot lacks the assignments' instrument kind, D-146); stored report parts are written again (D-145).

## A. Self-contained demonstration

### Start

```bash
# Dashboard: http://localhost:3000
cd apps/admin && npm ci && cp .env.example .env.local
#   in .env.local set ADMIN_MOCK_SCENARIO=approved_scores
#   on another port, set ADMIN_APP_ORIGIN to the exact address you open, for example http://localhost:3020
npm run build && npx next start -p 3000          # or: npm run dev

# Phone app in fixtures mode
cd apps/mobile && npm ci && cp .env.example .env  # EXPO_PUBLIC_DEV_FIXTURES=true is the default
npm run android                                   # or npm run android:dev for a development build
```

### Reset

- **Dashboard:** restart the server. The mock is one seeded synthetic population (160 households), so every start
  shows the same numbers; exports and closed privacy requests made during a session are forgotten.
- **Phone:** Settings → **Reset data** (under Sign out). Fixture data survives a reload and a
  language change; this puts every simulated account back to its starting state and signs you out.

### What is simulated

The dashboard labels its mock data; the phone does not label any of this on screen (D-142).

| Screen | What it is |
| --- | --- |
| Dashboard, every page | Mock data counted from one synthetic population, with the API's own definitions and privacy rules. The banner says so |
| Dashboard sign-in | A development sign-in: you pick a role, no identity is checked |
| Phone sign-in | Fixtures: a plain **Sign in** opens a simulated account choice ("Choose an account"), with no identity checked; it never names UAE PASS. Live stack: **Sign in with UAE PASS** opens UAE PASS *staging*, not production, though the button does not say so |
| Phone questionnaires | The platform's real draft content, byte for byte: the 39-item household assessment and the codebook's Module A |
| Phone result | Computed by `src/dev-fixtures/simulatedScoring.ts`, which mirrors the proposed, unapproved rule set. A real build computes nothing on the phone |
| Assistant and consultant | Simulated replies (the reply itself says so; the screen does not): fixtures mode connects no model. In the live stack they use OpenAI when the API has a key (below), and the screen does not say that answers go to OpenAI |
| Score report | A simulated report on the result screen, marked "[Simulated report for development]" in its own text, with one placeholder next step; its PDF works. In the live stack OpenAI writes it when the API has a key (below); the phone no longer says the answers and score are sent to OpenAI, nor names the model |
| Draft content | The questionnaires, consent wording and scoring rules are drafts, not approved, and parts of the family survey have no Arabic yet (shown in English): the phone says none of this |
| All services, organisations | The knowledge graph's 13 organisation codes with placeholder names marked "[DEV PLACEHOLDER]". The live stack shows the researched entries, not yet reviewed by the organisations (open question 50) |
| Demo buttons and demo family | Development-only shortcuts that make up answers or a family: see [Demonstration tools](#demonstration-tools-development-only) |
| Crisis support card | A simulated screen in fixtures mode: "kill myself" in the chat or the services search shows the card. The real screen runs on the server (D-135, D-138) |
| All services | Sample entries, marked as samples. The real directory is empty until someone approves entries |
| Dashboard safety alerts | Synthetic alerts, codes only. With `approved_scores` the two score alerts name mock results in the vulnerable band; without it there is no score, so no score alert |

### Sequence (about ten minutes)

Dashboard:

1. **Sign-in page.** Switch to Arabic and back. Point at the development sign-in and synthetic-data labels.
2. **Researcher → Overview.** Enrolment; assigned → started → completed; the trend with "Show as table"; the wave
   filter; the cells marked Hidden and the sentence that explains them (groups covering fewer than 10 people).
3. **Resilience analytics.** The household assessment's bands (Vulnerable, Transitional, Flourishing) and the
   comparison across the two waves. "Demographic comparisons" is empty on purpose: none is approved.
4. **Research exports.** Choose "Score results", tick the fields, request; it becomes Ready in about ten seconds.
   Download the CSV: pseudonyms, bands and months only; `excluded_count` and `incomplete_reason` say when a total was
   prorated.
5. **Sign out → Program administrator.** Assessment monitoring and Participation: the totals match the overview.
   Privacy requests: close one with an outcome.
6. **Sign out → System administrator.** Questionnaires → a version → its rule set (read-only). Household results.
   Safety alerts: the navigation badge, open before acknowledged before resolved; acknowledge one, resolve one with an
   outcome. They hold codes and a household reference, never what the person said.
7. **Arabic.** Switch on any page: the layout mirrors, charts keep their direction, digits stay Western.

Phone:

1. **Welcome.** Choose the language first.
2. **New participant:** **Sign in** → choose the new participant (simulated, not UAE PASS) → confirm details → Home.
3. **Household:** set up the household (make yourself head of household, or the result later says "shown to the head
   of household only") → add a member → review → confirm. Short of time: **Auto-fill** on the same
   screen creates a family of six with you as head and opens its roster; confirm it.
4. **Consent:** open the household assessment; starting is refused until participation consent is given.
5. **Answer** a few questions, go back and change one, leave. Progress is kept.
6. **Sign out → Household lead.** Continue the questionnaire and answer by hand (→ review answers → Submit), or press
   **Low**, **Medium** or **High** above the question: one tap answers, submits and opens the result with the total,
   band and grading scale. Then **Create my report** for the simulated report and **Download PDF**. To have it written
   as soon as the result opens, answer **Yes** to "Write your report as you answer?" on the question screen first.
7. **Home:** the family survey is done (Module A stays under All questionnaires) and **See next steps** opens the
   result; once the report is written, Home lists its next steps.
8. **New survey** under the result's buttons (or under the chart on Your family resilience) opens another round's
   questionnaire; press **High** and **Your family resilience** charts one more point. Repeat for more.
9. **All services, SOS, Ask Azwah, Settings.**

Avoid, or explain: Module A in Arabic shows some answer wording in English (the codebook has none; open question 31).
Finishing all 39 questions by hand takes about 75 taps; the demo buttons do it in one. Module A has no scoring proposal,
so its demo buttons say "Couldn't finish. Try again."; Home no longer waits for it (D-146).

## B. Live stack with the demonstration database

With Docker alone, the root README's "Quickstart" runs this stack in Compose, dashboard included (the API on port
8080 instead of 5080; `docker compose ... run --rm api db demo` builds the database and `USE_DEMONSTRATION_DATABASE=true`
points the API at it, D-150). The steps below run the API and the dashboard on this machine instead.

### Start

```bash
cp .env.example .env                      # set POSTGRES_PASSWORD and IDENTITY_PROVIDER_ADMIN_CLIENT_SECRET
npm run db:up                             # PostgreSQL only

cd backend/FamilyResilience.Api           # once: user secrets (backend/README.md)
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=azwah;Username=azwah;Password=<POSTGRES_PASSWORD>"
dotnet user-secrets set "Authentication:Schemes:Bearer:Authority" "http://localhost:5090"
dotnet user-secrets set "Authentication:Schemes:Bearer:RequireHttpsMetadata" "false"
dotnet user-secrets set "Authentication:Schemes:Bearer:ValidAudiences:1" "azwah-api"
dotnet user-secrets set "Exports:LocalDirectory" "<an absolute folder outside the repository>"
cd ../..

npm run demo:reset                        # builds azwah_demo from scratch, about 15 seconds
npm run identity:dev                      # identity provider, http://localhost:5090 (own terminal)
npm run demo:api                          # the API against azwah_demo, http://localhost:5080 (own terminal)
```

Dashboard, `apps/admin/.env.local` (it must run on port 3000: that is the redirect address the identity provider
knows):

```
ADMIN_ENVIRONMENT=development
ADMIN_AUTH_MODE=oidc
OIDC_ISSUER=http://localhost:5090
OIDC_CLIENT_ID=azwah-admin
OIDC_CLIENT_SECRET=<IdentityProvider:Clients:azwah-admin:Secret>
ADMIN_SESSION_SECRET=<base64 of 32 random bytes>
ADMIN_DATA_SOURCE=api
ADMIN_API_URL=http://localhost:5080
```

Phone, `apps/mobile/.env`: `EXPO_PUBLIC_DEV_FIXTURES=false`, `EXPO_PUBLIC_API_URL=http://localhost:5080`,
`EXPO_PUBLIC_OIDC_ISSUER=http://localhost:5090`, `EXPO_PUBLIC_OIDC_CLIENT_ID=azwah-mobile`, and
`EXPO_PUBLIC_DEMO_TOOLS=true` if you want the Low / Medium / High buttons. Start Metro (restart it after changing
`.env`: the values are fixed when the bundle is built), then
`adb reverse tcp:5080 tcp:5080` and `adb reverse tcp:5090 tcp:5090`. Sign in with a persona; UAE PASS staging needs
the sandbox client and a staging account on a phone, so do not rely on it on stage.

If another program holds a port, change it everywhere it appears; the issuer address must be identical for the API,
the dashboard and the phone.

The assistant, the consultant and the written score report use OpenAI only when the API has a key: `OPENAI_API_KEY`
in the repository root's `.env` (or `Assistant:Model:ApiKey` in user secrets), then restart `demo:api`
([`backend/README.md`](../backend/README.md)). Without one they say they are not switched on, and the result screen
says only that no explanation exists yet. Sending synthetic chat or answers to OpenAI is still sending them to a
provider that is not approved for family data (open questions 48 and 52); decide before you do it on stage. The crisis-phrase screen runs on
the server without a model, so a crisis phrase in the chat or the services search raises a safety alert and shows the
crisis support card with or without a key.

### Reset

`npm run demo:reset`, then restart `demo:api` (the reset ends its connections). It drops and rebuilds only the
`azwah_demo` database; the ordinary development database is never touched, and the command is refused outside
Development. Dates are relative to the day it is built and the open wave closes ten weeks later, so reset shortly
before presenting.

**If a database called `azwah_demo` already exists and was not made by this command** (for example the hand-made one
from earlier demonstrations), `demo:db` and `demo:reset` refuse and leave it untouched. Keep it by pointing
`ConnectionStrings:Default` at a differently named database for this work, or replace it deliberately with
`dotnet run --project FamilyResilience.Api -- db demo --reset --replace-foreign-database` from `backend/`.

### What the demonstration database holds

60 synthetic households (205 members, no names or contact details), a closed wave and an open one. In the closed wave
47 household assessments are completed and scored and 8 expired; in the open wave 40 are completed and scored, 8 are
in progress, 9 are not started and 1 is cancelled. Every answer went through the platform's domain and every score
through its own engine; a test recomputes each stored total independently from the stored answers.

**Approved in it, locally only:** the household assessment version 1, its scoring rule set, a rule that the head of
household answers it, and placeholder consent policies. Each records the source "LOCAL DEMONSTRATION ONLY (D-140): not
approved by a research owner". The family survey modules stay drafts: their Arabic is incomplete and they have no
scoring proposal. There are no referral rules, partners or support resources, so those screens show their empty
states.

**Safety alerts.** The build itself records none. Each seeded score leaves its `scoring.score_result_recorded.v1`
outbox message pending, and once `demo:api` is running its dispatcher raises a "low resilience score" alert (severity
high) for every seeded score in the vulnerable band, as the platform does for any approved score (D-136); each wave
holds at least ten of them. So Safety alerts fills with score alerts about synthetic households shortly after the API
starts, and a live answer that lands in the vulnerable band adds one more. Rehearse with them: this path has not been
run end to end on a live stack yet.

| Persona | Use it to show |
| --- | --- |
| `participant_lead` | The head of a household with a scored first round and the second round waiting: answer it live (or with Low / Medium / High in one tap), see the score, band and grading scale, the written report with a key, then refresh the dashboard; **New survey** then adds rounds for the chart |
| `participant_member` | The same household's spouse: no questionnaire, and the head's score is not given to them |
| `participant_new` | Live enrolment: create the household with yourself as head (or **Auto-fill**), confirm the roster, and the assessment is assigned |
| `researcher` | Overview, score distributions for both waves, exports |
| `program_administrator` | Monitoring; no scores |
| `system_administrator` | Household results, questionnaires and rules with their approval records |

### Sequence

1. Dashboard as **researcher**: overview and analytics for both waves; note the open wave's completed count.
2. Phone as **participant_lead**: Home → the household assessment → answer → review → submit (or **Low**, **Medium**
   or **High**, one tap) → total and band. Home shows the survey done and **See next steps**. Your family resilience
   now charts both rounds; **New survey** there or on the result opens another round, and its result adds a point.
3. Dashboard: refresh. Completed goes up by one; as **system administrator**, the new result is first in Household
   results, and Safety alerts lists the score alerts (one more if the new total is in the vulnerable band).
4. Dashboard as **researcher**: request and download an export.
5. Phone as **participant_new**: enrol (Auto-fill saves typing), confirm the roster, see the assignment
   arrive. Give participation consent, start it, and press **Low**: the result opens in the vulnerable band, and
   Safety alerts on the dashboard gains a score alert for it.
6. Dashboard → Questionnaires: open the household assessment's approval record and read its source aloud.

Each account can hold one household, and each submitted round stays submitted; **New survey** adds rounds, each a wave
of its own (`demo_wave_3`, `demo_wave_4`, …) that the dashboard's wave filter lists: `npm run demo:reset` (then restart
`demo:api`) before every rehearsal and before going on stage.

## Demonstration tools (Development only)

Two shortcuts exist for demonstrations only. Neither is a feature for families; say what they are when you use them.
The written score report beside them is a family feature, listed here because demonstrations lean on it.

| Tool | Where it is on the phone | When it shows | What it does |
| --- | --- | --- | --- |
| **Low / Medium / High** (D-130, D-143) | The question screen of a questionnaire, as three quiet text buttons with no card or explanation (D-142), above the progress bar | Fixtures mode always; API mode only with `EXPO_PUBLIC_DEMO_TOOLS=true` in `apps/mobile/.env` (in any build, so leave it `false` outside rehearsals). Disabled while a submission is waiting and once the time to answer has passed | One tap finishes the questionnaire: the server chooses the answers its approved scoring rules put in the lowest, middle or highest band, saves them, submits and scores the session like any answers, and the result opens (about 0.2 s on a local API). No review or submit step. In fixtures mode the fixture API does the same; against the API only while its `DemoTools:Enabled` is on, which only `appsettings.Development.json` sets (otherwise the screen says "Couldn't finish. Try again."; offline "No connection. Try again."). A deployed API refuses to start with it on (`demo_tools_enabled`), and the saved answers are audited as `sessions.demo_answers.saved` |
| **New survey** (D-144) | The result screen, under its buttons, and Your family resilience, under the chart, as a quiet text button | As Low / Medium / High | Opens a new round of the household assessment for the family at once (or the one still open) and its questionnaire, so its result adds a point to the chart. On the server it is a wave of its own that is never opened, assigned by the approved eligibility rules to this household only: no other household or result changes, though the dashboard lists the wave and counts its one result. Audited as `assignments.demo_check_in.opened` |
| **Auto-fill** (demo family, D-131) | "Set up your household", the first household screen, as a quiet text button under **Start**, with no notice (D-142); only for an account that has no household yet | Any development build (Expo Go from `npm run android`, or `npm run android:dev`), in either mode; never in a release build | Creates the household in one ordinary request: you (42, employed, head and main earner), a spouse (39), three children (19, 15, 10) and a parent (70), with no names. The roster opens with "Household set up. Check, then confirm."; confirm it to have questionnaires assigned. It calls the real household endpoint, so a development build must never point at a real server |
| **Written score report** (D-112, D-113; not a demonstration tool) | The result screen ("Result"), under the total, band and grading scale, for the head of household; and the choice "Write your report as you answer?" on the question screen | Fixtures mode: a simulated report. API mode: only when the API has an OpenAI key and the rule set has bands (the demonstration database's does) | Writes a compact report (D-145, about 150 to 210 words): a two-sentence summary, the two strongest and two weakest areas, and three numbered next steps with the organisations the knowledge graph reached (name, where they serve, services, website and phone; the full entries stay on All services), keeps it sealed on the phone and downloads it as a PDF. Home then lists its next steps (D-146). It says "Written by AI" (no model name) and changes no score; nothing on screen says the answers went to OpenAI |

Personas that can use them: in fixtures mode, **Household lead** has a household assessment in progress (demo
buttons, then the report), and **New participant** has no household (demo family; again after **Reset data**,
because fixture data survives a reload). On the demonstration database, `participant_lead` has the open wave's
assessment waiting (demo buttons with `EXPO_PUBLIC_DEMO_TOOLS=true`, the report with a key), and `participant_new` has
no household (demo family, then demo buttons once the roster is confirmed and consent given). New survey works for
either head after their first result. `participant_member` is not the head, so it sees no score, no report and no
chart.

A **Low** result on the live stack raises a score alert through the outbox while score alerts are on (D-136), as the
seeded vulnerable scores do; it is as synthetic as the score.

## Known limitations

- **Not approved.** Wording, reverse-scored items, bands for the family survey modules, eligibility and consent text
  all wait for named owners (open questions 1, 3, 16, 27 to 31, 60 to 67 in [`decisions.md`](decisions.md)).
- **Roster questions Q01 to Q03** of the codebook are not captured as questions: the roster holds members' birth
  months and employment, there is no primary-caregiver field, and "prefer not to answer" cannot be recorded there.
- **Production UAE PASS** is not onboarded; staging sign-in exists.
- **Partner referrals** are manual hand-offs; nothing is sent to a partner.
- **AI helpers** (the assistant, the consultant and the score report) use OpenAI only where the API has a key, and
  OpenAI is not yet approved to receive family data or answers (open questions 48 and 52). The organisations in the
  report's next steps and on All services come from public sources and have not been reviewed by them (open
  question 50); every card says to confirm before going. The crisis phrase list behind safety alerts is not clinically reviewed, and nobody is on a rota to
  respond to an alert (open questions 58 and 59).
- **A call about self-harm carries on** (D-141). OpenAI's own filter stops the model when someone speaks of hurting
  themselves; the phone shows the crisis card and SOS at once, the consultant is silent for about 4 to 6 seconds, then
  says a supportive reply and listens again. Its wording is engineering wording awaiting the owner's and a clinical
  review (open questions 53 and 58); do not demonstrate it with real distress.
- **Exports** carry session and score rows, not item-level answers.
- **No scientific validation** is claimed: software tests show the engine computes what the rule set says, not that
  the instrument measures resilience.
