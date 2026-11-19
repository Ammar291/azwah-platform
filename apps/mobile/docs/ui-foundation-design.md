# Mobile UI foundation: design

**Status:** designed and built on 2026-10-14 on `feat/mobile-ui-foundation`, autonomously at the owner's standing
request ("choose the best option and finish"). **Scope:** `apps/mobile` only. Shared documents (`CONTRIBUTING.md`,
`docs/*.md`) are not edited here; section 12 lists the edits and decision records the main implementation agent should
make.

**Superseded in part** by [`assessment-flow-design.md`](assessment-flow-design.md): where drafts live (M-12, M-13 by
M-35 to M-37), how they are saved and submitted (M-14 by M-39 to M-41) and what a complete result shows (M-15 by
M-46). M-9 is superseded by the OpenID Connect provider in `src/auth/oidc` (security hardening design, section
5.13): the placeholder now runs only when a build names no issuer.

## 1. Understanding

**Asked for:** the UI foundation of the participant app: design tokens and a replaceable brand, shared components,
the route structure with signed-in and signed-out groups, localization with right-to-left layout, an authentication
abstraction with a development mock kept apart from production OpenID Connect, a draft-storage abstraction on Expo
SQLite, loading, error and empty states, a component showcase, tests and README instructions.

**Constraints from the repository:**

- No approved questionnaire content, scoring rule set, consent text or result explanation exists (D-20, D-41, D-58,
  open questions 1–3, 9). Screens must show what the server returns and mark every placeholder.
- Identity (phase 1) is not built: there is no OIDC issuer and UAE PASS is not configured (D-6). Nothing may imply a
  working sign-in. *(Since superseded: OpenID Connect sign-in, D-83; UAE PASS staging through the development
  provider and the onboarding flow, D-96 to D-98.)*
- D-16 and the security model: the drafts table stays empty until on-device encryption is decided (open question 7).
  No participant data on the device yet.
- The participant API exists (D-47 to D-62). Response shapes come from `backend/openapi/v1.json`.
- Clients never compute scores or eligibility, and show server problems as translated states, never server text.
- No "approved screen inventory" document exists in the repository; the route list in the request is used as the
  inventory (section 5).

**Success:** every route renders in Arabic and English from translated resources; a developer can walk the whole
participant journey (sign in, create a household, confirm the roster, consent, answer, review, submit, see the result
state, history) against development fixtures; the same screens run against the real API once phase 1 supplies tokens;
`npm run typecheck && npm run lint && npm test && npx expo export --platform android --platform ios && npm run doctor`
passes.

## 2. Decisions

| # | Decision | Why |
| --- | --- | --- |
| M-1 | No new npm packages. Icons are drawn with views; UUIDs use `crypto.randomUUID` when present, else a v4 fallback | Every requested capability is covered by installed packages; fewer native modules keep Expo Go working |
| M-2 | `Text`, `Button` and `Card` become `AppText`, `AppButton` and `AppCard` (names asked for; no clash with React Native's own `Text` and `Button`). `Screen`, `FormField` and `LikertScale` keep their names | `CONTRIBUTING.md` and `docs/architecture.md` need the new names (section 12) |
| M-3 | Brand values live in one file, `src/design-system/brand.ts`; `tokens.ts` derives semantic roles (primary, secondary, surfaces, text, status) from it | Replacing the brand edits one file; components use roles, never palette names |
| M-4 | Input and selectable borders use `borderStrong` (`#7d8781`, 3.7:1 on white). The old `line` colour (1.4:1) is kept for decorative card borders only. Warning text darkens to `#7a5408`. A unit test checks every text and control pairing against WCAG 2.2 (4.5:1 text, 3:1 controls) | The previous input border failed WCAG 1.4.11 |
| M-5 | Touch targets: 48 points for buttons, options and header actions (`minTouchTarget`), above the 44-point floor in `CONTRIBUTING.md` | Request asks 44–48; 48 is Android's guidance and suits older participants |
| M-6 | One stack navigator, no tab bar. Home is a card dashboard that links to every area. Custom `AppHeader` with a direction-aware back chevron | No icon library (M-1); fewer nested navigators; RTL stays correct |
| M-7 | Route guards with `Stack.Protected`; the unguarded `app/index.tsx` is the anchor that redirects by auth state (`entryHref`). `redirectTo` is SDK 58+ | When a guard turns false, Expo Router returns to the anchor route |
| M-8 | One switch, `EXPO_PUBLIC_DEV_FIXTURES=true`, selects **development fixtures mode**: the development mock sign-in plus the in-memory fixture API. Honoured only in `__DEV__`; release builds always use API mode | The mock issues no token, so it can only ever talk to fixtures; a single switch cannot be half-set |
| M-9 | API mode uses the OIDC placeholder provider, which reports "sign-in unavailable (not configured)". Phase 1 replaces it with authorization code + PKCE and tokens in `expo-secure-store` behind the existing `CredentialStore` interface | Honest about phase 1; no unused native dependency now |
| M-10 | The development mock stores only the chosen persona code in `settings` (no token exists), so a language reload keeps the person signed in | The language switch reloads the app (D-15) |
| M-11 | A 401 from the API ends the session as `session_expired`: the guard sends the person to sign-in with an explanation. Leaving the signed-in state clears the query cache; signing out also clears local drafts. An expiry keeps drafts for the same person to resume | Shared phones; resuming after expiry |
| M-12 | Drafts: `DraftStore` interface with SQLite and in-memory implementations. SQLite drafts are used **only in development fixtures mode** (synthetic data). API mode keeps drafts in memory until open question 7 is decided. Neither claims encryption: `encryptedAtRest` is `false` on both | D-16 and the security model forbid participant answers on the device before the encryption decision |
| M-13 | Local migration 2 drops the never-written `drafts` placeholder and creates `draft_answers` keyed by (session, item) with a per-row revision and sync state | Identifies drafts by session and question; upsert prevents duplicates; revisions stop a slow sync from marking a newer answer as synced |
| M-14 | Answers save locally at once, then sync to `PUT /sessions/{id}/responses` after a short pause, through **one `AnswerSync` per session** shared by every screen showing it; submission waits for every write and save, then sends an `Idempotency-Key` kept for retries of that submission. Drafts the server refuses for good (400, 404, 409) are discarded and the person is told; network trouble, 401 and 403 keep them | The server is the record (D-52, D-54); the device copy covers dropped connections; a refused draft must never block submission |
| M-15 | Result screen: shows the server's state. `pending` and `incomplete` are explained; `complete` shows the score and tier code as recorded, with a marked notice that how results are explained to families awaits approval | No approved bands or explanations (open questions 2, 3) |
| M-16 | Problem codes from ProblemDetails become translation keys under `problems.<code>`; anything unknown falls back to a message per error kind. Server `title` and `detail` are never read | Security rule: never show server text |
| M-17 | Dates and numbers: `Intl` with `ar-AE`/`en-AE` and the Latin numbering system, then any Arabic-Indic digit is normalised to Western digits | `CONTRIBUTING.md`: Western digits in both languages; Hermes' Intl may ignore `-u-nu-latn` |
| M-18 | Component showcase: the existing development-only route `/dev/design-system`, extended to every component. No Storybook | Storybook for React Native adds native and Metro setup; the in-app gallery is lightweight and shows real RTL |
| M-19 | `react/jsx-no-literals` (strings in JSX children) is an error in `app/`, `src/features` and `src/components` | Enforces "no hard-coded user-facing text in feature components" |
| M-20 | Draft-store tests run on real SQLite through Node 24's `node:sqlite` behind the `SqlDatabase` interface | Tests the actual SQL (upserts, partial indexes, migrations) without the native module |
| M-21 | Directional icons mirror from the **measured** native layout direction (`LayoutDirectionProvider`: an invisible marker at `start: 0`), falling back to `I18nManager.isRTL` | Found on the emulator: after an in-app switch to Arabic, Expo Go laid out right to left while `I18nManager.isRTL` stayed `false`, so chevrons pointed the wrong way |
| M-22 | The development-mode label is one slim line on every screen, not a full notice | Found on the emulator: the full notice took a third of the question screen |
| M-23 | Drafts belong to one account: the token subject is kept as `drafts_owner`; signing in as anyone else deletes them first, signing out deletes them once running writes finish. Only `sessions.not_in_progress` and `assignments.assignment_expired` among 409s discard drafts; other conflicts are retried | Review of PR #4: an expired session must not hand one person's answers to the next, and `persistence.*` conflicts are temporary |

## 3. Folder structure

| Folder | Holds |
| --- | --- |
| `app/` | Routes only (Expo Router). Thin screens that compose features |
| `src/design-system` | Brand, tokens, fonts and translation-free components (strings come in as props) |
| `src/components` | App-level components that use translations: `LanguageSwitcher`, `AssessmentCard`, `QueryView`, `ProblemState`, `RouteErrorBoundary` (the development banner went in D-142) |
| `src/features/<area>` | Feature UI and hooks: home, household, consent, assessments, sessions, history, profile, help, status |
| `src/auth` | Auth types, context, the development mock, the OIDC placeholder, provider selection, `entryHref` |
| `src/api` | HTTP client, health, participant schemas (Zod), `ParticipantApi` interface and HTTP implementation, query hooks |
| `src/drafts` | `DraftStore` interface, SQLite and memory stores, selection policy, save-status model |
| `src/storage` | SQLite opening, migrations, settings; `testing/` adapters |
| `src/i18n` | Languages, resources, direction, `useLocalization`, formatting |
| `src/dev-fixtures` | The only place mock data may live: personas, the placeholder instrument and the in-memory fixture API |
| `src/app-shell` | Start-up and provider composition |
| `src/test-utils` | Render helpers with every provider |

## 4. Design system

Tokens: colours (semantic roles from the brand), spacing (4–48), radii, typography (display, title, heading, body,
bodyStrong, label, caption; Markazi Text and Readex Pro both cover Arabic), `minTouchTarget` 48, `maxFontScale` 2
(text scales with the system setting up to 200%).

| Component | Variants and behaviour |
| --- | --- |
| `AppText` | Variants from the type scale; tones default, secondary, muted, primary, success, warning, danger, inverse |
| `AppButton` | primary, secondary, tertiary, danger; `loading` (busy, disabled, spinner); full-width by default |
| `AppInput` | Label, hint, error (announced as an alert), disabled, focus border; numeric keyboards |
| `AppCard` | default, muted, highlight; pressable when given `onPress` (button role, chevron) |
| `ListGroup`, `ListRow` | Related links on one surface with hairline dividers; each row one labelled button with a chevron (D-118) |
| `AppHeader` | Title (header role), back button with direction-aware chevron, optional end action |
| `Screen` | Safe area, scroll, optional keyboard avoidance (`padding` on iOS), optional fixed footer |
| `FormField`, `SwitchField` | React Hook Form bindings for `AppInput` and a labelled switch |
| `RadioCardGroup` | Large radio cards with label, description, check mark and border change (never colour alone) |
| `LikertScale` | `RadioCardGroup` for questionnaire options; non-substantive options sit after a divider |
| `ProgressIndicator` | Text plus bar, progressbar role with min, max and now |
| `Badge`, `Notice`, `Icon` | Status label with glyph; inline info/warning/danger/placeholder banner; drawn icons that mirror in RTL |
| `Section`, `DetailRow` | Titled group with a header role; one labelled value, stacked or inline, read as "label: value" |
| `LayoutDirectionProvider` | Measures the native layout direction for `Icon` (M-21) |
| `LoadingState`, `ErrorState`, `EmptyState` | Whole-area states; strings passed in |
| `ConfirmationDialog` | Modal with scrim, title, message, confirm and cancel; destructive variant; back button cancels |

## 5. Routes (the screen inventory)

| URL | File | Guard |
| --- | --- | --- |
| `/` | `app/index.tsx` | Anchor: redirects by auth state |
| `/welcome` | `app/(public)/welcome.tsx` | Signed out |
| `/auth/sign-in` | `app/(public)/auth/sign-in.tsx` | Signed out |
| `/consent` | `app/consent.tsx` | Signed in |
| `/home` | `app/(app)/home.tsx` | Signed in |
| `/household/create` | `app/(app)/household/create.tsx` | Signed in |
| `/household/[householdId]` | `app/(app)/household/[householdId]/index.tsx` | Signed in |
| `/household/[householdId]/members` | `app/(app)/household/[householdId]/members.tsx` | Signed in |
| `/assessments` | `app/(app)/assessments/index.tsx` | Signed in |
| `/assessments/[assignmentId]` | `app/(app)/assessments/[assignmentId].tsx` | Signed in |
| `/sessions/[sessionId]` | `app/(app)/sessions/[sessionId]/index.tsx` | Signed in |
| `/sessions/[sessionId]/review` | `app/(app)/sessions/[sessionId]/review.tsx` | Signed in |
| `/sessions/[sessionId]/result` | `app/(app)/sessions/[sessionId]/result.tsx` | Signed in |
| `/history` | `app/(app)/history.tsx` | Signed in |
| `/profile` | `app/(app)/profile.tsx` | Signed in |
| `/help` | `app/(app)/help.tsx` | Signed in |
| `/dev/design-system` | `app/dev/design-system.tsx` | Development builds only |
| anything else | `app/+not-found.tsx` | None |

The request's `/( app )/…` is the `(app)` group. Route IDs in URLs are display handles only: the server authorises
every request from the token and its own records, and answers 404 for anything the caller may not see.

**Recovery.** The `(public)` and `(app)` layouts and `consent` export a translated `ErrorBoundary` with a retry. The
root layout's boundary is bilingual inline text, because translations may not be loaded when it fails. API failures
render through `QueryView`: loading, a translated problem with a retry, 404 as "not found", 403 for missing
participation consent as a link to consent. A 401 never shows a screen of its own: it ends the session (M-11).

## 6. Authentication

```ts
type AuthState =
  | { status: 'initializing' }
  | { status: 'signed_out'; reason: 'none' | 'signed_out' | 'session_expired' }
  | { status: 'signed_in'; session: { subject: string; personaCode: string | null } }
  | { status: 'unavailable'; reason: 'not_configured' };

interface AuthProvider {
  readonly kind: 'development_mock' | 'oidc';
  getState(): AuthState;
  subscribe(listener: () => void): () => void;
  restore(): Promise<void>;
  signIn(request: SignInRequest): Promise<void>;
  signOut(): Promise<void>;
  getAccessToken(): Promise<string | null>;
  reportUnauthorized(): void;
}
```

The development mock offers personas from `src/dev-fixtures` ("new participant", "household lead"), returns no access
token, and can simulate an expired session from Profile. Every mock screen carries a "Development sign-in (simulated).
No identity is verified." notice and a development banner. The OIDC placeholder is always `unavailable`; the sign-in
screen says sign-in is not set up in this build.

## 7. Data

`ParticipantApi` lists the calls the screens need (me, household, members, roster confirmation, consent policies and
records, assignments, published versions, sessions, responses, submission, result, history). `createHttpParticipantApi`
calls `/api/v1/…` with the bearer token from the auth provider and validates every response with Zod schemas written
from `backend/openapi/v1.json`. `createFixtureParticipantApi` (in `src/dev-fixtures`) keeps a per-persona in-memory
world with a short artificial delay, a synthetic household and a `[DEV PLACEHOLDER]` instrument whose every string is
marked as a placeholder. Fixture submissions leave results `pending`, as the real API does without an approved
scoring rule set.

## 8. Drafts

`draft_answers (session_id, item_id, option_id, revision, updated_at, sync_state, synced_at)`, primary key
`(session_id, item_id)`, `WITHOUT ROWID`, partial index on pending rows. `saveAnswer` upserts and bumps the revision;
`markSynced` marks only the revisions that were sent; `pending` lists unsent answers; `discardSession` and `clearAll`
delete. The session screen merges server answers with pending local ones (local wins: it is newer) and reports a save
status: saving, saved on this device, kept in memory, saved, not saved (will retry) or not accepted (choose again).
"Change this answer" on the review screen records the requested question and goes back to the questionnaire under
it, rather than opening a second one.

## 9. Localization and direction

`useLocalization()` returns `t`, `language`, `direction`, `isRTL`, `changeLanguage`, `formatDate`, `formatDateTime`,
`formatNumber` and `pickText` (chooses the Arabic or English of a server `TextDto`; falls back to English only where
the server allows Arabic to be missing, which is never for approved content, D-40). Layout uses start/end only;
`Icon` mirrors directional glyphs from the actual layout direction (Expo Go stays LTR, D-15).

## 10. Accessibility

Roles and labels on every control; headers marked; errors announced as alerts; save status and connection state in
polite live regions; progress exposed as a progressbar with values; status always text plus glyph; 48-point targets;
text scales to 200% with layouts that wrap; contrast checked by test.

## 11. Verification on a device

Walked through on the Android emulator in Expo Go (development fixtures mode): welcome in both languages, simulated
sign-in, home, questionnaire detail, answering with device drafts and sync, review, "change this answer", submission,
the pending result, profile, the language switch, a cold restart (persona and language kept) and the component gallery
in a right-to-left layout. It found and fixed: the emblem ignoring its width (`aspectRatio` on an Android `Image`), the
oversized development notice (M-22), a doubled heading and repeated language name on Profile, a doubled full stop in
review labels read by screen readers, and the chevron direction (M-21).

## 12. Testing

Unit and component tests with Jest and React Native Testing Library: every design-system component, contrast,
localization and formatting, auth providers and `entryHref`, the HTTP client and participant API (fetch stubs), Zod
schemas against contract-shaped payloads, draft stores on real SQLite (the same contract suite for SQLite and memory),
migrations on real SQLite, save-status rules, fixture API flows, and screen tests for sign-in, household creation,
consent, answering with drafts, review and submission, and result states.

## 13. For the main implementation agent (shared documents)

Recorded in `docs/decisions.md` as D-63 to D-66 (with the household enrollment's D-67 to D-69):

- **D-63 Mobile UI foundation.** M-1 to M-7, M-18, M-19, M-21, M-22: design system names and brand file, single stack with guards,
  the in-app showcase.
- **D-64 Mobile modes and sign-in.** M-8 to M-11: development fixtures mode, the mock and the OIDC placeholder, expiry.
- **D-65 Mobile drafts before the encryption decision.** M-12 to M-14, M-23: SQLite drafts only for synthetic fixtures data, owned by one account;
  memory drafts against the API until open question 7 is decided; `draft_answers` schema; sync and idempotent
  submission.
- **D-66 Mobile results and problem codes.** M-15 to M-17.

A finding for D-15: on the Android emulator with Expo Go (SDK 57), switching language **in the app** did produce a
right-to-left layout until the next cold start, but `I18nManager.isRTL` kept reporting `false` (and the locale
`en_US`). A cold start returns to left to right. Development builds remain the place to check RTL; M-21 keeps icons
correct either way.

Applied in PR #4 (factual renames only): the component names in `CONTRIBUTING.md` ("Mobile UI requirements") and the
design-system row of `docs/architecture.md` section 5.

Done after #2 and #3 were merged: `src/api/participant/ParticipantApi.ts` is now `participantApi.ts` (the mobile
naming rule: components in PascalCase, other modules in camelCase).

Edits still to make:

- `CONTRIBUTING.md`, "Mobile UI requirements": touch targets are 48 points (the component names are done).
- `docs/architecture.md` section 5: the rest of the folder table in section 3 above.
- `docs/development-plan.md`, phase 6: the mobile answer flow exists against fixtures and the API contract; offline
  device drafts wait for the encryption decision.
