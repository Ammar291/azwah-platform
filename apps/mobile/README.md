# Azwah mobile app

Expo SDK 57, React Native 0.86, Expo Router, TypeScript (strict). Runs on Android and iOS from one codebase.
Rules: [`CONTRIBUTING.md`](../../CONTRIBUTING.md) (mobile UI and right-to-left sections). Design and decisions of the UI
foundation: [`docs/ui-foundation-design.md`](docs/ui-foundation-design.md).

## Set up

    npm ci
    cp .env.example .env      # see "Modes" and the API address below

| Where the app runs | EXPO_PUBLIC_API_URL |
| --- | --- |
| Android emulator | `http://10.0.2.2:8080` |
| A phone on the same Wi-Fi | `http://<this computer's LAN IP>:8080` |

For a physical phone, allow the API port through the Windows firewall once (administrator PowerShell):
`netsh advfirewall firewall add rule name="Azwah API 8080" dir=in action=allow protocol=TCP localport=8080 profile=private`.

**Sign-in against the Azwah identity provider** (API mode, `EXPO_PUBLIC_DEV_FIXTURES=false`):

1. Start it: the `identity-provider` Compose service or `backend/FamilyResilience.IdentityProvider` (port 5090, see its
   README), with the API trusting it as its authority. **UAE PASS staging** is the real sign-in it offers (D-96,
   D-129): set the public sandbox client as its README says, and sign in
   with a UAE PASS staging app account. In Development it also offers simulated personas (you pick a made-up person and
   no identity is checked), which deployed providers never do. Production UAE PASS is not onboarded.
2. Set `EXPO_PUBLIC_OIDC_ISSUER=http://localhost:5090` and `EXPO_PUBLIC_OIDC_CLIENT_ID=azwah-mobile` (as in
   `.env.example`). The issuer must match exactly what the provider announces.
3. Let the phone reach `localhost:5090`. Android emulator or USB phone: `adb reverse tcp:5090 tcp:5090` (and
   `adb reverse tcp:8080 tcp:8080` if you use `EXPO_PUBLIC_API_URL=http://localhost:8080`). Otherwise opt in to the
   provider listening on the network (`IDENTITY_PROVIDER_BIND=0.0.0.0` in Compose) and use the computer's LAN address as
   the issuer everywhere: the provider's `IdentityProvider:Issuer`, the API's authority and `EXPO_PUBLIC_OIDC_ISSUER`.
4. The sign-in callback comes back to `azwah://auth/callback` in a development build and to
   `exp://<host>:<port>/--/auth/callback` in Expo Go; the provider accepts both in Development.

A release build refuses an `http` issuer, an issuer whose discovery says `azwah_development_only`, and an `http`
`EXPO_PUBLIC_API_URL`. A demonstration build (`EXPO_PUBLIC_DEMONSTRATION_BUILD=true`, D-148) accepts the
development-only issuer, never `http` (see "Shareable demonstration APK").

**Continue with Google (D-147).** When the provider has a Google OAuth client (its README, "Google sign-in"), its
discovery lists `google` in `azwah_identity_providers` and the sign-in screen adds **Continue with Google** under the main
button; it sends `idp_hint=google` for that request only. No app setting is needed, and no button appears while the
provider has no Google client. Sign-out after a Google sign-in opens no end-session page.

## Modes

| `EXPO_PUBLIC_DEV_FIXTURES` | Sign-in | Data | Drafts |
| --- | --- | --- | --- |
| `true` (development builds only) | **Simulated**: a plain "Sign in" opens a simulated account choice ("Choose an account") with two made-up accounts; it names no UAE PASS and, since D-142, says nothing about being simulated; no identity is checked, no token exists | Fixture API (`src/dev-fixtures`), sealed in settings so a reload and sign-out keep it; reset from Settings | Encrypted on the phone (see Drafts) |
| anything else, and every release build | OpenID Connect through the system browser when `EXPO_PUBLIC_OIDC_ISSUER` and `EXPO_PUBLIC_OIDC_CLIENT_ID` are set; otherwise sign-in says it is not available | The real API (`/api/v1`) | Encrypted on the phone (see Drafts) |

| OpenID Connect variable | Meaning |
| --- | --- |
| `EXPO_PUBLIC_OIDC_ISSUER` | The issuer, exactly as its discovery document states it. `https` only, except `http` in development builds |
| `EXPO_PUBLIC_OIDC_CLIENT_ID` | The public client (`azwah-mobile` at the Azwah identity provider). There is no client secret |
| `EXPO_PUBLIC_OIDC_SCOPE` | Default `openid offline_access`; `openid` is always added |
| `EXPO_PUBLIC_OIDC_PROMPT` | Default `login`, so a shared family phone asks again at every sign-in |
| `EXPO_PUBLIC_OIDC_RESOURCE` | Optional RFC 8707 resource indicator for the API |
| `EXPO_PUBLIC_OIDC_IDP_HINT` | An override (D-142). Empty: "Sign in with UAE PASS" sends `idp_hint=uae_pass` (with `ui_locales`) for that request only, so the browser opens on UAE PASS staging, and a development build against a development provider also offers **Use a test account** (the provider's own page, no hint). A value such as `uae_pass`: every sign-in sends it and no test accounts are offered. `none`: no hint, a plain "Sign in", no UAE PASS named |

The Azwah identity provider (`backend/FamilyResilience.IdentityProvider`) signs families in with UAE PASS **staging**
and its public sandbox client in every environment; simulated personas are its Development extra (D-96, D-129).
Production UAE PASS is not onboarded, and nothing in the app claims otherwise. Signing out of a UAE PASS session also
ends the UAE PASS browser session: the app opens the provider's end-session endpoint with `idp_hint=uae_pass` and
returns on `auth/signed-out`; a cancelled or failed browser never blocks the sign-out (D-129).
With the provider in Development the sign-in screen also offers, unless `EXPO_PUBLIC_OIDC_IDP_HINT` is set, **Other
sign-in options** under the UAE PASS button: the provider's own page with its test accounts.

**No labels on screen (D-142).** At the owner's direction this build is for a recorded demonstration on synthetic
data, so the phone no longer labels simulated, staging, development or not-yet-approved behaviour, nor says that
answers go to OpenAI: no development banner, no "(staging)" on UAE PASS, no "simulated" notices on the assistant or
the consultant, no OpenAI disclosure before the score report, no "Arabic awaiting approval" or "awaiting legal review".
This README and [`docs/demo-guide.md`](../../docs/demo-guide.md) are where those facts are recorded now. The labels
and the disclosure must be restored before any real family uses the app (open question 69, threat T-35).

Fixtures mode shows no banner (D-142). The fixture questionnaires are the platform's own
draft content: byte-for-byte copies of `backend/FamilyResilience.Infrastructure/Content` under
`src/dev-fixtures/content` (a test fails when they drift), served with status `draft` because nothing is approved. The
household assessment (39 statements, seven areas) is scored on submission by `src/dev-fixtures/simulatedScoring.ts`,
which mirrors the brief's proposed, unapproved rule set the way the server's strategy would (D-99); the codebook's
Module A stays "awaiting result", because no scoring proposal exists for it. Nothing outside `src/dev-fixtures`
computes a score. Two personas: **Household lead** (household, consent, one assessment finished and scored, one in
progress, Module A not started) and **New participant** (no household yet). Screen tests do not use this content:
they pass the fixture API a small test-only placeholder catalog (`src/test-utils/placeholderContent.ts`, marked
`[DEV PLACEHOLDER]` in every string) so they stay short and deterministic. The codebook has no Arabic for some of
the family survey's texts (`ar: null`, left as they are): they show in English, unlabelled since D-142. A draft version's description (the family survey's is
the codebook's subtitle) is not shown; an approved version's is.

The question screen's three quiet demo buttons ("Low", "Medium", "High", D-130; no card or explanation since D-142)
finish the questionnaire in one tap (D-143): the server chooses every answer so the result lands in that band, saves
them, submits and scores the session, and the result opens over Home; the phone chooses and saves nothing, and there is
no review or submit step. In fixtures mode the fixture API does the same with `src/dev-fixtures/simulatedDemoAnswers.ts`;
against the API (with `EXPO_PUBLIC_DEMO_TOOLS=true`) the server does, from its approved rules, and only while its
`DemoTools:Enabled` is on (Development only). They are switched off while a submission waits and once the time to
answer has passed; offline they say so and send nothing.

With demo tools on, **New survey** (a quiet text button under the result's buttons and under the resilience chart,
D-144) asks the server for a new round of the household assessment at once and opens its questionnaire; Low, Medium or
High then finish it, and the chart gains a point without a restart. The server's round is a wave of its own, assigned by
its approved rules to this household only.

Home (D-146) counts the household assessment as the family survey (`instrumentKind` on each assignment), so a module still
open does not hold the path back; once the survey is done, "See your next steps" opens the latest result, and Home lists
the next steps of its report (sealed on the phone, marked "Written by AI") until routed support exists. The report is
compact (D-145): the strengths of the two areas standing highest, the weaknesses of the two lowest and three steps, each
organisation by name, coverage, services and contacts; the full entries stay on All services.

**The fixture data survives a reload and sign-out.** The fixture API seals its worlds (households, consent, sessions,
answers, results, privacy requests) into the app's settings (`dev_fixture_worlds`) after every response and reads them
back when it is created, so the reload that applies a language and a switch between simulated accounts keep a
demonstration. The snapshot is one value encrypted with the answers' AES-256-GCM cipher (`src/storage/protection`)
under a keystore key of its own, `azwah.dev_fixture_worlds.key.v1`, because every sign-out deletes the drafts key.
Without a working keystore nothing is kept on the phone: the worlds live in memory, like the drafts. The made-up
answers stay on the phone (encrypted, or only while the app runs) after submitting and signing out, until they are
reset; a questionnaire's introduction in fixtures mode says nothing about the phone, because the real build's wording
would not be true there. **Settings → Reset data** (under Sign out) asks first, puts every persona back to its seed, deletes the setting and its key, then signs out
(local drafts and the chat go the usual way), and the sign-in screen says the reset is done. Release builds have
neither.

No screen shows the server connection (D-142). The component gallery is opened at `/dev/design-system` in a
development build.

For demonstrations, the household set-up screen of a development build offers a quiet **Auto-fill** text button under Start (D-131, D-142): one press
creates the household with a made-up family of six (fixture API in fixtures mode, the real API in API mode) and opens
its roster, ready to confirm. One household per account still applies, so use an account without one: in fixtures
mode the New participant persona, again after Settings → **Reset data** (fixture
data survives a reload); in API mode `participant_new` on the demonstration database after `npm run demo:reset`
(D-140), or on a fresh development database (`npm run db:reset`, `db:migrate`, `db:seed`). Release builds never show
it (D-131), except a demonstration build (D-148, "Shareable demonstration APK").

## Run

- **Android:** `npm run android` (starts Expo and opens Expo Go on the running emulator or a USB device).
- **iPhone:** install **Expo Go** from the App Store, put the phone on the same Wi-Fi, run `npm start` and scan the QR
  code with the Camera app. Windows cannot build iOS apps; Expo Go is enough for development. Store builds use EAS
  Build later and need an Apple Developer account (open question 8 in `docs/decisions.md`).
- If Expo Go reports an incompatible SDK, the store version has moved past SDK 57: use a development build
  (`npx expo run:android`, or EAS for iOS) or upgrade the SDK in a planned change.

Changing the language reloads the app so the layout direction can change; that is expected.

**Arabic right-to-left layout does not appear in Expo Go.** Expo Go (SDK 57) disables RTL and resets the layout
direction for every project it opens, so in Expo Go Arabic text shows in a left-to-right layout. Check RTL in a
development build instead: `npm run android:dev` (add `-- --port 8088` if port 8081 is taken), or an EAS development
build for iPhone. Expo Go also ignores the app's own reload request (`reloadAppAsync` resolves and nothing happens),
so there the language changes in place, without a reload. Elsewhere the app gives up after one reload attempt per
language, and if a requested reload never comes, the language switch works again after 5 seconds.

**Windows and native builds.** The Android native build fails with "Filename longer than 260 characters" when the
project sits in a deep folder (the C++ build embeds the full path twice). Build from a short path, for example a clone
at `C:\src\azwah`, then `cd apps\mobile && npm run android:dev -- --port 8088`. A `subst` drive or a junction does not
help: Node resolves it back to the long path and the build then mixes the two roots.

### Shareable demonstration APK

One sideloadable release APK for other people's phones, talking to the owner's laptop, which runs the API and the
identity provider in **Development** (synthetic demonstration data only, `npm run demo:api`) behind a public HTTPS
tunnel such as Tailscale Funnel (D-148). `EXPO_PUBLIC_DEMONSTRATION_BUILD=true` lets this release build accept the
development provider, offer **Other sign-in options** (its persona page) and **Auto-fill**; `http` addresses and
fixtures mode stay refused, and nothing on screen says it is a demonstration (D-142). Never use it for a build real
families use (open question 69, threat T-39).

1. Work from a short-path clone (for example `C:\src\azwah`; the Windows 260-character limit above), with `npm ci` run
   in `apps/mobile`.
2. In `apps/mobile/.env` set the tunnel's addresses (both `https`; the issuer exactly as the provider's discovery
   states it):

   ```
   EXPO_PUBLIC_API_URL=https://<laptop>.<tailnet>.ts.net:8443
   EXPO_PUBLIC_OIDC_ISSUER=https://<laptop>.<tailnet>.ts.net
   EXPO_PUBLIC_OIDC_CLIENT_ID=azwah-mobile
   EXPO_PUBLIC_DEV_FIXTURES=false
   EXPO_PUBLIC_DEMONSTRATION_BUILD=true
   EXPO_PUBLIC_DEMO_TOOLS=true
   ```

3. `npx expo prebuild --platform android --no-install` (it rewrites the `android` script in `package.json` to
   `expo run:android`; put it back with `git checkout -- package.json`)
4. `cd android && ./gradlew assembleRelease` (`.\gradlew.bat assembleRelease` in PowerShell)
5. The APK is `android/app/build/outputs/apk/release/app-release.apk`. It is signed with the debug keystore that
   prebuild sets up: fine for sideloading (each phone must allow installing from unknown sources), never for a store.

Expo inlines the variables when it bundles, so changing one means building again. Anyone with the tunnel's address can
sign in as a persona, and every assistant answer, consultant call and report spends the laptop's OpenAI key: set a
spending cap on that key before sharing the APK.

## Structure

| Path | Holds |
| --- | --- |
| `app/` | Routes only (Expo Router); thin screens that compose features |
| `src/design-system` | `brand.ts` (the replaceable brand), `tokens.ts`, fonts and translation-free components |
| `src/components` | App components that use translations: `AppScreen`, `LanguageSwitcher`, `SignOutDialog`, `AssessmentCard`, `QueryView`, `ProblemState`, `RouteErrorBoundary` |
| `src/features/<area>` | Screen content and hooks: navigation (the side menu), home, emergency, assistant, consultant, resilience, household, consent, assessments, sessions, history, profile (Settings), help, auth |
| `src/auth` | `AuthProvider` interface, the development mock, the "not configured" placeholder, `useAuth`, `entryHref`, the deep-link rewrite (`nativeIntent.ts`); `oidc/` holds the OpenID Connect provider (config, PKCE, discovery, callback, tokens, keystore, native parts) and its test doubles in `oidc/testing` |
| `src/api` | HTTP client, health, participant Zod schemas (from `backend/openapi/v1.json`), `ParticipantApi` and its HTTP implementation, query hooks |
| `src/drafts` | `DraftStore` interface (drafts, session copies, queued submissions), the encrypted SQLite and memory stores, which one runs, save status |
| `src/storage` | SQLite, migrations (`PRAGMA user_version`), settings; `protection/` seals answers (AES-256-GCM, key in the keystore); `exportFiles.ts` holds a data export only while it is shared |
| `plugins` | Local config plugins: `withRecentsScreenshotDisabled` (Android 13+ keeps no Recents screenshot) |
| `src/network` | The connection (expo-network), wired to TanStack Query's `onlineManager` |
| `src/i18n` | Languages, resources (`en.json`, `ar.json`), direction, `useLocalization`, date and number formatting |
| `src/dev-fixtures` | The only place mock data may live |
| `src/app-shell` | Start-up, service wiring by mode, provider composition, the navigator inside the lock (`AppShell`) |
| `src/security` | The inactivity lock: `lockTimes.ts` (its durations), the controller, its provider and the phone-lock adapter; test doubles in `testing/` |
| `src/test-utils` | Render helpers (`createTestProviders` also wraps the real router of `expo-router/testing-library`), the Expo Router mock, an encrypted store on real SQLite, a switchable connection, fixture worlds |

### Routes

Signed out (`app/(public)`): `/welcome`, `/auth/sign-in`, `/auth/uae-pass` (development fixtures only: the simulated
UAE PASS). First sign-in (`app/(onboarding)`): `/confirm-details`, `/account-ready`. Signed in: `/consent`, and in
`app/(app)`: the main places of the side menu in `(drawer)` (`/home`, `/assistant`, `/consultant`, `/resilience`,
`/services`, `/settings`), and stacked over them `/household/create`, `/household/create/about-you`, `/household/[householdId]`, `/household/[householdId]/members`,
`/household/[householdId]/members/add`, `/household/[householdId]/members/[memberId]`,
`/household/[householdId]/review`, `/household/[householdId]/confirm`, `/household/[householdId]/assignments`,
`/assessments`, `/assessments/[assignmentId]`, `/sessions/[sessionId]`, `/sessions/[sessionId]/review`,
`/sessions/[sessionId]/result`, `/history`, `/help`, `/emergency` (a modal). Development only: `/dev/design-system`. Anything else:
`+not-found`.

The root layout's navigator (`src/app-shell/AppShell.tsx`) guards the groups with `Stack.Protected`. `(onboarding)` is
open while `GET /api/v1/me` says `registeredAt: null` (and while "account ready" shows); `(app)` is open otherwise. The
signed-in guards are also closed while the app is locked, and the registration check waits for unlocking. When a guard
closes (sign-in, sign-out, expired session, confirming details, lock) Expo Router returns to `app/index.tsx`, which
redirects by auth and onboarding state (and shows nothing while locked). Each group exports a translated
`ErrorBoundary`; the root's is bilingual inline text. IDs in routes are only handles: the server decides access.

## Design system

Use `src/design-system`: `AppText`, `AppButton` (primary, secondary, tertiary, danger, loading), `AppInput`,
`FormField`, `SwitchField`, `AppCard` (default, muted, highlight, pressable), `ListGroup` and `ListRow` (grouped links),
`AppHeader`, `Screen`, `Section`, `RadioCardGroup`, `LikertScale`, `ProgressIndicator`, `Badge`, `Notice`, `Icon`,
`DetailRow`, `LoadingState`, `ErrorState`, `EmptyState`, `ConfirmationDialog`, and `LayoutDirectionProvider` at the root.

- **Brand:** change `brand.ts` (palette, emblem, font names), `fonts.ts` and the icon and splash in `app.json`.
  Components use the semantic roles in `tokens.ts` only. Pine (primary) is the working colour; gold and sand are
  brand furniture, never text, data or status.
- **Accessibility:** 48-point targets, roles and labels on every control, status as words plus a glyph, text that
  scales to 200%, input borders at 3:1. `tokens.test.ts` checks every colour pairing against WCAG 2.2.
- **Showcase:** run a development build and open `/dev/design-system`. It shows every component, live, in the current language and
  direction. No Storybook.
- **Answers outside the scale** (`LikertScale`): kept exactly where the version puts them, with their own label and
  value; drawn as a quieter card, with a divider before them when the scale comes first.
- **Icons** are drawn with views (no icon font). Forward and back chevrons and the sign-out arrow mirror when the
  native layout runs right to left, measured by `LayoutDirectionProvider` (Expo Go can lay out RTL while `I18nManager.isRTL` says otherwise).

## Text and languages

All user-visible text comes from `src/i18n/resources/{en,ar}.json`; add every key to both files. Screens use
`useLocalization()` for `t`, the language and direction, `formatDate`, `formatNumber` (Western digits in both
languages) and `pickText` (the API's bilingual text). Lint forbids string literals in JSX under `app/`,
`src/features` and `src/components`. Tests check that both files have the same keys, that Arabic values are Arabic
script, and that every key the code uses exists. Server problems show as translated messages chosen by the
ProblemDetails `code` (`problems.*`) or the error kind (`errors.kinds.*`); server text is never shown.

## Household enrollment

Set up (`/household/create`, then "about you", which creates the household with the caller as its first member), the
roster (add, edit, remove after a confirmation), review, confirmation and the assignments the server returns. Design,
decisions and the safe-retry rules: [`docs/household-enrollment-design.md`](docs/household-enrollment-design.md).

- The server decides eligibility. Confirming calls `POST /households/{id}/confirm-roster`; the app shows the
  assignments it returns and has no control that changes them.
- The roster rules (one head, one main earner, adults only in those roles, a relationship for everyone but the head,
  a work status for adults) only guide the forms (`src/features/household/rosterRules.ts`); the server checks them
  again and its problem codes are shown in words.
- No names, identity numbers, household-size number or "children under 18" question: the count and the presence of
  children come from the roster (D-24).
- Retrying never repeats a change: after a failure that may have reached the server, the next attempt reads what the
  server holds first (`src/features/household/rosterRequests.ts`).

## Side menu

The app's layout is a side menu (D-106, `src/features/navigation/SideMenu.tsx`, Expo Router's Drawer in
`app/(app)/(drawer)/_layout.tsx`). It lists Home, Azwah assistant, Azwah consultant, Your household, Your family
resilience, All services and Settings, with the signed-in name and household reference at the top and **Sign out** at
the bottom (it asks first, with the same `SignOutDialog` as Settings). The open place is marked by a bar and a tint;
the assistant and the consultant say "Not available yet" while `GET /assistant/availability` says no model is
connected. Each place sets `menu` on `AppScreen`, so its header shows the menu button instead of back; screens opened
from a place stack on top with a back button.

- **Your family resilience** (`/resilience`) gathers the family survey: questionnaires still to answer, the family's
  resilience over time, past questionnaires (each opens its result; only the head of household sees the score and band,
  D-103) and the next steps.
- **Your resilience over time** (`src/features/resilience/ResilienceTrendCard.tsx`, D-132) draws
  `GET /me/resilience-trend`: the household assessment's recorded totals, one point per round, oldest at the start, on
  the scale and bands the server sends. The line breaks where the server starts a new segment (another rule set); the
  latest round is shown above the chart, a tap (or a screen reader) chooses another, and "Show every round" lists them
  all. Anyone but the head of household is told the chart is the head's. It is drawn with `react-native-svg` (installed
  with `npx expo install`), which has no layout direction: `trendGeometry.ts` measures from the start edge and
  `toSvgX` mirrors it right to left; labels and touch targets are views with `start`/`end`.
- **All services** (`/services`) is the searchable list SOS opens, then the approved support directory from the server
  (`SupportDirectory`, D-110): the whole list, or the server's search once two characters are typed and the person
  pauses. Entries open in the in-app browser.
- **Azwah assistant** (`/assistant`, D-108, D-122, D-123) is a chat. The conversation is kept on the phone only
  (`src/features/assistant/chatStore.ts`: the `assistant_messages` table with each message's `channel`, `chat` or
  `voice`, and the reply's suggested screens and sources in `extras_cipher`, plus one `assistant_memory` row, all
  sealed like drafts; 200 messages at most; deleted at sign-out and when another account signs in). Each question sends
  the memory and the latest 40 messages (`conversation.ts`) to `POST /assistant/replies`; the server adds what it holds
  about the family, and from 30 messages returns a refreshed memory that replaces the kept one. Under a reply the app
  shows the screens it suggests as buttons with the app's own labels (`SuggestedActions`, `suggestedScreens.ts`) and
  the directory entries it names as cards opening their https link. One question is in flight at a time; a question
  is kept on the phone before it is asked, so a reply is never kept without it. A question or a reply the phone could
  not keep says something went wrong, and a question left unanswered when the app closed says so; both offer Try
  again. A 503 means the API has no OpenAI key (D-119).
- **Azwah consultant** (`/consultant`, D-109, D-124) is a live spoken conversation over the WebSocket
  `/api/v1/consultant/live`, protocol version 2 (`consultantConnector.ts`): the first message is
  `{"type":"start","memory":…,"messages":[…]}` with the chat's memory and latest 40 messages, `ready` says
  `"protocol":2` and 24 kHz both ways, and the server may send `action` (shown as buttons; opening one ends the call).
  The microphone streams continuously, as silence while muted. The phone's audio is a `VoiceAudio`
  (`voiceAudio.ts`, `useVoiceAudio.ts`, D-125): the native `AzwahVoice` module (`modules/azwah-voice`: echo
  cancellation and gapless playback, so the microphone stays open while the consultant speaks) in the Azwah app build,
  and in Expo Go an expo-audio fallback (`expoVoiceAudio.ts`: 24 kHz `useAudioStream`, WAV segments with a longer first
  one) that sends silence instead of the reply's echo unless the person clearly talks over it (`echoGate.ts`), and
  suggests headphones. The whole call is captioned; when it ends, however it ends, its lines are added to the chat
  (`channel: voice`). Leaving the screen ends the call. The microphone is asked for on "Start talking". While it
  connects, **End** stops waiting, and after 10 seconds without the server's `ready` the call fails with the
  connection message; whatever an abandoned attempt says later is ignored.
- **Development fixtures** answer the assistant with a reply that says it is simulated (the screens themselves carry
  no "simulated" notice since D-142; SOS for a few obvious danger words, the sample debt-advice
  entry when debts or the budget come up, and a simulated memory from 30 messages), speak protocol 2 for the
  consultant (a greeting that says what the call started with, playback of what was said after a pause, and one
  example button), and list three sample directory entries, named as examples, saying they are not real services, and
  linking to example.org (`src/dev-fixtures/fixtureAssistant.ts`, `simulatedConsultant.ts`). Against a real API both
  use OpenAI when the API has a key (D-119, D-124). Fixtures also write a score report marked as simulated.
- **Result** (`/sessions/[sessionId]/result`) shows the head of household the grading scale the server sends
  (`ScoreScale`, D-111) and a written report (`src/features/score-report`, D-112, D-113). If the person agreed on the
  questionnaire (`ReportPreparationChoice`), `reportPreparer.ts` has written it in the background a section at a time from
  the answers the server confirmed (`score_report_parts`, sealed), and `assembleReport` joins it with the recorded score at
  once, with no model call. Otherwise "Create my report" writes it whole (`POST /sessions/{id}/report`, 100-second limit).
  `NextStepsFlow` shows the family's numbered next steps with the organisations and procedures from the knowledge graph
  (D-114, D-115). All services lists the same organisations (`SupportAuthorityList`), each with Azwah's own symbol
  (`authorityMarks.ts`), drawn as SVG on screen and in the PDF. It is kept
  sealed (`score_reports`) and downloaded as a PDF made on the phone (`reportDocument.ts` builds the page, `reportFiles.ts`
  prints and saves it). In API mode the report uses OpenAI when the API has a key (D-112), like the assistant.
- **Settings** (`/settings`) replaces Profile: account, language, consent, help, the person's data and sign out; in
  development fixtures also a quiet **Reset data** under Sign out (D-142).

The drawer needs `react-native-gesture-handler` (installed with `npx expo install`; plain npm pulls a version newer
than SDK 57 supports) and the gesture root in `app/_layout.tsx`. Jest runs it with the stand-ins in `jest.setup.ts`.
`renderRouter` turns on fake timers: read fixtures before it and set up `userEvent` with
`advanceTimers: jest.advanceTimersByTime`.

## Home

Home (D-100 to D-102, D-105) leads
with "Your family's path": five steps from what the server says (household confirmed, consent given, the family survey
open or completed, support routed, the next family survey in about six months), ordered by `buildFamilyPath`
(`src/features/home/familyPath.ts`). The first step not done is current and carries its button; the family survey
step says it is the most important one, because the family's next steps are based on its answers. "Your next steps"
lists the support categories the server routed (`GET /api/v1/me/recommendations`) as recorded, codes and referral
status only; with none routed it says where they will appear, and once the family survey is finished, that no support
has been routed to the family. "See all questionnaires" stays on the survey step whenever the family has any
questionnaire, so a roster edit (which makes the household step current again) never hides the way back to one in
progress. The app never chooses, ranks or explains a step.

Two buttons float over Home (`FloatingActions`, through the `overlay` slot of `AppScreen`): **SOS** at the bottom start
opens `/emergency`, and **Ask Azwah** at the bottom end opens `/assistant`. Start and end follow the language.

- **Emergency numbers** live in the app (`src/features/emergency/emergencyNumbers.ts`), so they work offline. Each
  entry records its official source and the date it was checked; review them at least every six months (open question
  45). A tap opens the dialler with the number filled in (`tel:`); nothing is called automatically, and a device
  without a dialler shows the number to dial by hand. A search field below "Dial 999" finds a service by name,
  description, emirate or number in either language (`emergencySearch.ts`, D-105).
- **Ask Azwah** opens the Azwah assistant (D-108; see Side menu). It answers once a model is connected; until then
  the screen says it is not available yet.

## Sign-in and tokens

Screens depend on `AuthProvider` only. The development mock remembers just the persona code (in `settings`) so the
language reload keeps you signed in (the fixture data is kept there too, see Modes); it has no tokens. A 401 from the
API ends the session as expired: the person lands on sign-in with an explanation. Signing out clears cached data and local drafts, and for a UAE PASS session also
ends the UAE PASS browser session through the identity provider (D-129). Production UAE PASS is not onboarded.

The flow (D-98): the native splash, then the opening sequence (`src/features/splash`: "Welcome to Azwah", then the
wordmark and pillars; it waits for the sign-in state and the registration answer, and only fades with reduced motion);
the welcome screen with "Sign in with UAE PASS", which also creates the account (D-142; a "Connecting
to UAE PASS" overlay shows meanwhile); for someone the server has not registered, "Confirm your details" with what UAE
PASS released (masked by the provider; nothing is sent to the API but the confirmation, D-97) and "Account ready";
then Home. "These aren't my details" explains and signs out. The screens were designed in Google Stitch (the design
files are not included in this repository).

The OpenID Connect provider (`src/auth/oidc`; D-83 in [`docs/decisions.md`](../../docs/decisions.md)):

- **Sign-in:** authorization code with PKCE (S256), random `state` and `nonce` (256 bits each, expo-crypto), in the
  system browser (`expo-web-browser`: ASWebAuthenticationSession with an ephemeral session on iOS, Custom Tabs on
  Android). Discovery must name the configured issuer; the callback must come back to the redirect URI with the same
  `state` and the issuer's `iss`; the ID token's `iss`, `aud`, `azp`, `exp`, `iat`, `nonce` and `sub` are checked.
  Its signature is not checked on the phone: it comes straight from the token endpoint over TLS (OIDC Core 3.1.3.7).
  Profile claims (`name`, `given_name`, `gender`, `birthdate`, `azwah_profile`) become an `IdentityProfile`
  (`src/auth/identityProfile.ts`), kept in memory with the session and returned by the first renewal after a restart.
- **Storage:** the refresh token and the subject in expo-secure-store (`WHEN_UNLOCKED_THIS_DEVICE_ONLY`); access and
  ID tokens in memory only. Never SQLite, settings, logs or files (`services.test.ts` checks).
- **Renewal:** 60 s before expiry, one renewal at a time, keeping the rotated refresh token. A refused refresh token
  (`invalid_grant`) ends the session as expired; no network is `ApiError('network')`, so the person stays signed in
  offline and requests retry. Start-up waits at most 5 s for the first renewal.
- **Sign-out:** a best-effort `POST /api/v1/me/sign-outs`, revocation of the refresh token, the keystore entries
  deleted, then local data as above. A best-effort `POST /api/v1/me/sign-ins` follows each sign-in.
- **Deep links:** `app/+native-intent.tsx` keeps Expo Router off `auth/callback` and `auth/signed-out`.
- **Errors:** cancelling says nothing; a failed check, an unreachable provider and a refusal each have their own
  translated message. A 429 from the API is `rate_limited`, retried with backoff.

## App lock and privacy cover

D-91 in [`docs/decisions.md`](../../docs/decisions.md).

- **Privacy cover:** whenever the app is `inactive` or in the `background`, signed in or not, the brand mark covers
  everything, so the app switcher's snapshot shows no answers. Android takes its Recents snapshot before JavaScript
  can draw the cover, so on Android 13 and later the local config plugin `plugins/withRecentsScreenshotDisabled.js`
  calls `setRecentsScreenshotEnabled(false)` in `MainActivity.onCreate` (Recents shows a blank card). Screenshots are
  not blocked (`FLAG_SECURE` is a spec non-goal); older Android versions rely on the cover alone.
- **When it locks** (while someone is signed in): on coming back after **2 minutes** or more away from the foreground,
  after **10 minutes** without activity in the foreground (a touch, a new screen or an answer chosen, so keyboard and
  screen-reader users count too; time away does not count), and when a start restores a session. The reload that
  applies a language chosen in the last 30 seconds is not a new start and does not lock. The durations live only in
  `src/security/lockTimes.ts`; nothing is configurable from the environment.
- **Clocks:** time is measured on the wall clock and on a steady clock (`performance.now()`), and whichever says more
  counts. A wall clock set back while the app was away locks it at once; the steady clock may not count time the phone
  sleeps, so it only ever adds to the wall clock.
- **Locked:** the signed-in route group's guard is closed, so none of its screens stays mounted; only the lock screen
  shows, and the navigator is hidden from screen readers.
- **Unlock** asks the phone's own lock through expo-local-authentication: Face ID, Touch ID or the fingerprint
  prompt, with the device passcode as fallback. Unlocking returns once to the screen the person was on (kept in
  memory only; any later visit to the start route leads home). Cancelling or failing keeps the lock and says so
  quietly; the message is announced to VoiceOver and TalkBack.
- **Sign in again** signs out after a confirmation and so deletes answers not yet sent (D-70; the lock screen says so),
  then the person signs in as usual. If signing out fails, the lock stays and the screen says so. On a phone with no
  screen lock at all it is the only choice, and the screen says why.
- Development fixtures mode locks the same way (with no banner since D-142); the module ships
  in Expo Go. A development or release build uses the `faceIDPermission` text in `app.json`, in English only: an
  Arabic `NSFaceIDUsageDescription` would need Expo's `locales`, which adds an `ar.lproj` to the iOS project, and
  React Native then lays the whole app out right to left on Arabic iPhones whatever language is chosen in the app
  (`RCTI18nUtil` `isApplicationPreferredLanguageRTL`), the problem of D-15.
- Not covered: a confirmation dialog (a native modal) open when the app leaves the foreground stays above the cover in
  the snapshot (dialogs hold no answers); screenshot blocking (`FLAG_SECURE`) is not built (spec non-goal).

## Your data (Settings)

Spec §6 (mobile). The schemas here match the API's (`backend/openapi/v1.json`).

- **Download a copy of my data:** the screen first says what the file holds and that where it goes is the person's
  choice. The app checks the phone can share, reads `GET /api/v1/me/data-export` (Zod-validated; 10 an hour, with
  its own message when the limit is reached; offline it fails at once and is never sent later on its own), writes the
  body as the server sent it, indented, to a file in the cache folder `azwah-exports` (expo-file-system's `File` and
  `Directory`), opens the share sheet (expo-sharing, `application/json`, `public.json`) only if the app is still
  unlocked and in the foreground at that moment, and deletes the file afterwards, also when sharing fails or is
  cancelled. The folder is also emptied before each export, at start-up, at sign-out and whenever the app locks, so a
  share sheet left open under the lock has nothing left to send. On Android the share call returns when the chooser
  closes, so a receiver that reads the file later may find it gone. The export is never cached. Scores and support
  recommendations are not in it (D-72).
- **Ask about your data:** access, correction or erasure, about the person signed in or a member they act for
  (`actableMemberIds`); a confirmation; then `POST /api/v1/me/privacy-requests`. No free text. The list (`GET
  /api/v1/me/privacy-requests`, 20 at a time) shows type, member, status, resolution and dates. One open request per
  member and type (`privacy.request_already_open`), `households.member_not_found` and rate limits are explained in
  words.
- The fixture API supports both; the household lead has one earlier request the programme team closed, and requests
  made in fixtures mode stay open.

## Answering, drafts and offline

Design and decisions: [`docs/assessment-flow-design.md`](docs/assessment-flow-design.md) (D-70 to D-72).

- **Introduction** (`/assessments/[assignmentId]`): name, the version's own description (not for a draft), number of
  questions, round and dates, a privacy notice, start or continue. No duration (the API gives none). A round whose
  code numbers itself (`…wave-2`) reads "Round 2"; any other code is shown as recorded.
- **Questions** are rendered from the server's schema by `QuestionRenderer`, one explicit case per item type. Only
  `single_choice` exists in the contract; any other type shows "needs a newer app" and cannot be answered. Previous
  and Next ignore presses until the question they moved to is on screen and for 350 ms after, so a double tap never
  skips a question.
- **Saving:** a choice is written to the phone at once and sent after a short pause through one `AnswerSync` per
  session (`src/features/sessions/answerSync.ts`). One save at a time, already-saved answers are not resent, failures
  retry with jittered exponential backoff, nothing is sent offline, and the save line shows the last time the server
  accepted answers.
- **Conflicts:** a draft whose question changed on the server since it was chosen is never sent over the newer
  answer; the person keeps one or the other.
- **Closed:** a 409 `sessions.not_in_progress` or `assignments.assignment_expired` (the wave closed; the server keeps
  saying so after maintenance abandons the session) closes the session on the phone for good: unsent answers are
  removed and counted, nothing is retried, and a queued submission stops with its reason instead of retrying. After
  expiry the questionnaire and its review say the time to answer has passed and take no more answers or submission.
- **Offline:** a session opened once while signed in is kept as an encrypted copy and can be answered offline after a
  restart. A session never opened on the phone cannot be.
- **Submission** is queued on the phone with its `Idempotency-Key` (`submissionQueue.ts`) and sent when signed in and
  online, with the same key on every retry and after a restart. The app says "submitted" only after the server
  confirms; the questionnaire and its review then leave the stack and the result opens over Home, so Back goes Home
  (`useLeaveFlow`, Expo Router's `dismissTo`). Confirming the roster leaves its set-up screens the same way.
- **Results:** the head of household sees the total score and the band, as the server returns them (D-103); the server
  leaves the values out for everyone else, and the app says the score is shown to the head only. Area scores and
  support recommendations stay hidden until their display is approved (`resultDisclosure.ts`); the app never computes
  them.

**Protection on the phone.** Answers and session copies are encrypted field by field with AES-256-GCM (expo-crypto).
The key is random, made on the phone, and kept only in the Keychain or Keystore (expo-secure-store,
`WHEN_UNLOCKED_THIS_DEVICE_ONLY`). The database file itself is not encrypted: IDs, times and sync states are readable.
Without a working keystore, answers stay in memory and the app says so. Signing out deletes local data and the key.
Both modules ship in Expo Go.

## Check

    npm run typecheck
    npm run lint        # includes the rules against left/right styles and hard-coded text
    npm test            # Jest + React Native Testing Library; draft stores run on real SQLite (node:sqlite) and real AES-GCM (node:crypto)
    npx expo export --platform android --platform ios   # proves both bundles compile
    npm run doctor
