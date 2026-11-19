# Assessment taking: design

**Status:** built on 2026-10-15 on `feat/mobile-assessment-flow` (from `main` at `9daba5d`), at the owner's standing
request to choose the best option and finish. **Scope:** `apps/mobile`, plus the decision records and the security
model's "data on a lost phone" row (section 10). The backend is unchanged.

## 1. Understanding

**Asked for:** the complete assessment-taking experience on the participant app, covering:

- an introduction;
- a schema-driven question renderer;
- fixed questionnaire versions;
- autosave with local drafts, safe synchronisation, retries, deduplication, conflict handling and recovery after a
  restart;
- offline answering and offline submission for sessions already authorised;
- review and submission with an idempotency key;
- server-authoritative results;
- every listed error state;
- tests.

The request also asks to define and test how answers cached on the phone are protected, without assuming SQLite
encryption.

**What exists already** (foundation and household enrollment, D-63 to D-69):

- Answering one question at a time, with progress, back and next.
- A review screen.
- Submission with an `Idempotency-Key` reused on retry.
- A result screen.
- One `AnswerSync` per session, which writes each choice locally and then sends it after a pause.
- Drafts in SQLite, in development fixtures mode only, in memory against the API (D-65).

**What the platform defines** (contract `backend/openapi/v1.json`, Sessions module):

- `ItemType` has one value, `single_choice`. An answer is `{ itemId, optionId }`, and the Sessions module "must not
  accept free-text answers".
- `PUT /sessions/{id}/responses` takes 1 to 200 answers. It replaces them and returns every answer with
  `lastModifiedAt`. It takes no precondition (no `If-Match`, no version).
- `POST /sessions/{id}/submit` requires an `Idempotency-Key`:
  - a retry with the same key answers as the first request did;
  - a session submitted by another request answers `already_submitted`;
  - a key reused for another request answers 409 `requests.idempotency_key_reused`;
  - a failed request stores no key (D-54).
- A session keeps its instrument version for its whole life. Answers are checked against that exact version
  (`sessions.version_mismatch`). The published-version endpoint serves Approved and Retired versions, so a session
  whose version was retired can still be answered and submitted. A new session starts only on an Approved version
  (`sessions.version_not_published`). Submitted answers never change.
- Submission scores and routes in the same transaction (D-52). With no approved scoring rule set the result is
  `pending`.
- `GET /me/recommendations` returns category codes only.
- No participant-facing explanation of scores, tiers or support categories is approved (open questions 2, 3 and 13).

## 2. Decisions

The decisions continue the numbering of the earlier mobile notes (M-1 to M-34).

| # | Decision | Why |
| --- | --- | --- |
| M-35 | **Answers on the phone are encrypted field by field** with AES-256-GCM (expo-crypto's native implementation). A random 256-bit key is made on the phone and kept only in the platform keystore (expo-secure-store: iOS Keychain `WHEN_UNLOCKED_THIS_DEVICE_ONLY`, Android Keystore). Each ciphertext carries associated data naming its table, session and question, so a value copied to another row fails to decrypt. The database file is not encrypted (no SQLCipher), so IDs, timestamps, revisions and sync states are readable. If the keystore or the cipher is missing, or a start-up self-test fails, nothing sensitive is written to the phone: drafts stay in memory and the app says so. This applies in both modes | Answers the drafts part of open question 7 (section 3). Restart recovery and offline submission need a device copy, and SQLite encryption cannot be assumed. Both modules ship in Expo Go, so no development build is needed |
| M-36 | **Crypto-shredding.** Signing out, or another account signing in, deletes every local row and then the key. A new key is made at the next write. `PRAGMA secure_delete` is on | Remnants in free pages or the write-ahead log cannot be decrypted once the key is gone |
| M-37 | **Three local tables** (local migration 3): `draft_answers` (one per session and question, answer encrypted), `session_copies` (session, assignment, published version and the server's answers, encrypted) and `pending_submissions` (session ID, idempotency key, times and attempts; no answers). Migration 3 replaces the version 2 `draft_answers`, which only ever held synthetic fixture data (D-65) | The offline view and the submission must both survive a restart |
| M-38 | **Offline covers only what was already authorised and downloaded.** A session copy is written only after its session, assignment, version and answers were read from the server, signed in, by the account that owns the drafts. It is used only when the server cannot be reached (no connection, a timeout, or queries paused offline). A 404 deletes it. Starting a session always needs the server | Offline must never widen access. A 404 means access ended (D-48) |
| M-39 | **Saving.** Each choice is written to the phone at once: one upsert per question, so a later choice replaces an earlier one, and nothing is kept only in memory. The server save waits 800 ms after the last choice ("debounced"). One save runs per session at a time (flushes are chained). An answer equal to the server's is marked as saved without being sent. Unsent answers keep their session's sync alive with no screen open, and answers left by an earlier run are sent when the app resumes. Failures retry automatically with exponential backoff and full jitter: a random delay up to `min(60 s, 2 s × 2^attempt)`, at most 6 attempts, then a manual "Try again". Attempts restart when the connection returns or the app comes to the foreground. Nothing is sent while offline. A 401 or a 403 stops automatic retries. The time of the last successful save is kept and shown | "Debounced local saves" would risk losing a choice made just before the app is killed. For tap-to-choose questions, the immediate write per question is strictly safer and costs no more |
| M-40 | **Conflicts.** Each draft records the server's `lastModifiedAt` for its question when it was chosen (its base). Before sending, a draft is compared with the server's latest answer. If that question changed on the server since the base, and to a different option, the draft is a conflict: it is not sent. Times are compared as instants, because a save's response carries the server clock's 100 ns and a read PostgreSQL's microseconds. The person chooses "keep the answer from this phone" (re-based and sent) or "use the saved answer" (the draft is deleted). Submission waits until no conflict is left. A save this phone made itself re-bases newer drafts of the same question, so it is not mistaken for a conflict. The server's latest answers come from the screen's read, from each save's response, or from a read before sending whenever they may be stale (first save of a run, after the phone was offline, after a network failure, back in the foreground) | The server stays the record, and stale drafts never overwrite newer answers silently. **Limitation:** the save endpoint takes no precondition, so a change made elsewhere between that read and the save is overwritten (last write wins in that window). A precondition on `PUT …/responses` would close it; this is proposed for the backend (section 10) |
| M-41 | **Submission is a request queued on the phone.** It is written with a new idempotency key before anything is sent, and it survives a restart when device storage is protected. It is processed when signed in, online, and when every answer has been saved without conflict. Every retry uses the same key. One attempt runs per session at a time, and the button is disabled while one waits or runs. The app says "submitted" only after the server's 200. While a submission waits, answers cannot be changed; it can be cancelled while it is not being sent. Outcomes are in section 6 | Prevents duplicate submissions across taps, retries and restarts, and never claims success early |
| M-42 | **Versions.** The app loads exactly the session's version (`versionNumber`) and checks `versionId == session.instrumentVersionId`. A mismatch, or a 404 for the version, shows "this questionnaire version isn't available" and nothing else. No other version is ever substituted, and answers are never moved between versions. A session copy whose version differs from the server's session is discarded | The backend's explicit policy: a running session keeps its version, retired or not, and new sessions need an approved version |
| M-43 | **Question types.** The renderer is a registry with explicit support per type. `single_choice` is the only type the contract permits, and the only one built. An item of any other type parses as `unsupported`: the introduction says the app needs an update, and the questionnaire cannot be started, answered or submitted. Multiple choice, text and numeric answers are not built | They need a contract (answer shape, validation, storage, scoring) that does not exist. The backend refuses free text, and inventing a shape would contradict it |
| M-44 | **Validation.** Zod schemas are built from the published version: an answer's option must belong to its question, and required questions must be answered. A draft that fails (for example a stale one) is discarded and the person is asked to choose again. React Hook Form is not used in the questionnaire | Each choice saves as it is made, so there is no form to submit. React Hook Form stays in the household forms |
| M-45 | **Introduction.** It shows the name and section, the version's description as its purpose (server text), the number of questions and how many are optional (from the version), the round and dates, a privacy notice, and start or continue. No duration is shown | The API gives no duration, and an estimate from the question count would not be defensible. The privacy notice states only what the platform implements, and its wording joins the consent text in legal review (open question 9) |
| M-46 | **Results.** Showing the score, the tier and support recommendations to participants is off until approved (`RESULT_DISCLOSURE` in `src/features/sessions/resultDisclosure.ts`). The result screen shows the completion and the server's result state, never values. Turning a flag on shows the API's value exactly as returned. The app never computes or infers a score or a tier | No bands, explanations or category wording are approved (open questions 2, 3 and 13). Supersedes D-66's "a complete result shows the score and tier code as recorded" |
| M-47 | **Connectivity.** expo-network events feed TanStack Query's `onlineManager` and the sync engine. Requests that fail with a network error count too. Local reads use `networkMode: 'always'` | Queries pause offline instead of failing, so screens need a defined offline state |

## 3. Protecting answers on the phone

| Data | Protected device storage | Without protection |
| --- | --- | --- |
| A draft answer (the option chosen) | `draft_answers.answer_cipher`, AES-256-GCM, associated data `azwah/v1:draft/<session>/<item>` | Memory only |
| The session copy: session, assignment, version, server answers | `session_copies.content_cipher` and `answers_cipher`, associated data `azwah/v1:copy/<session>/content` and `…/answers` | Memory only |
| A queued submission: session, idempotency key, times, attempts | `pending_submissions`, in clear (it holds no answers) | Memory only |
| The key | Keychain or Keystore, this device only, readable while unlocked | None |

**Envelope:** `v1:` followed by base64 of the 12-byte IV, the ciphertext and the 16-byte tag.

**Start-up:** the keystore must be available, and a round trip with a fresh test value must succeed. Otherwise
the store is memory only (`persistence: 'memory'`), and the save line says answers are kept only while the app is open.

**A key that is gone while rows remain** (for example after the keystore was reset): those rows can never be
read. They are deleted, and the person is told that some answers saved on this phone could not be read. Answers the
server already has are unaffected.

**What the protection is not:** it does not hide that a session exists or which questions have a draft. It does not
protect a phone that is unlocked and compromised. It does not replace device encryption. Android cloud backup stays
off.

**Tests** (`src/storage/protection`, the store contract):

- The same envelope code runs on Node's AES-GCM.
- Stored rows never contain the plaintext option ID.
- A value moved to another row fails, and a tampered value fails.
- A lost key gives a reported loss, not a crash.
- Clearing deletes the key.
- A store opened again on the same database and keystore reads the drafts back.

## 4. Structure

| Path | Holds |
| --- | --- |
| `src/storage/protection/` | `utf8`, `base64`, the `AesGcm` and `SecretStore` interfaces with their Expo implementations, `createFieldCipher`, `resolveDeviceProtection` |
| `src/storage/testing/` | `nodeAesGcm` (Node's crypto) and `memorySecretStore` |
| `src/drafts/` | The `DraftStore` interface (drafts, session copies, queued submissions), the encrypted SQLite and memory stores, the save-status model |
| `src/network/connectivity.ts` | The `Connectivity` interface, the expo-network implementation, the `onlineManager` binding and a fake for tests |
| `src/features/sessions/` | `retryPolicy`, `answerSync` (conflicts, backoff, server knowledge), `submissionQueue`, `assessmentEngine` (one per app, holds every session's sync and the queue), the questionnaire renderer, the screens, `resultDisclosure` |

## 5. Data flow

1. **Introduction** (`/assessments/[assignmentId]`): the assignment, its published version, and any queued
   submission. Start calls the server. Continue opens the session.
2. **Session** (`/sessions/[sessionId]`): `useSessionContent` reads the session, the assignment, the version and the
   answers from the server. When all four arrive, it checks the version and writes the encrypted copy. When the
   server cannot be reached, it uses the copy and says so. It never mixes server and copy data.
3. **Choosing:** the choice goes to `AnswerSync.choose`, which records the draft with its base, then schedules the
   save. The save classifies each draft as same, conflict or send, sends in chunks of 200, marks only the sent
   revisions, re-bases newer drafts, and stores the server's answers in the copy with the time.
4. **Review:** the merged answers, missing required questions with a way to each, conflicts, and the save state.
   Submit asks for confirmation, then `SubmissionQueue.submit`.
5. **Queue:** queued, then sending answers, then submitting, then submitted. On success it deletes the session's
   drafts, copy and queued record and refreshes participant data. If the review screen is open, it goes to the
   result.
6. **Result:** `GET /sessions/{id}/result`, shown under `RESULT_DISCLOSURE`.

## 6. Error states

| State | Detected by | Shown as | Saved answers |
| --- | --- | --- | --- |
| No connection | expo-network offline, a network or timeout error, queries paused | Offline banner; save line "saved on this phone, sent when you're back online"; a queued submission says "not submitted yet" | Kept |
| Session expired | 401 (the API reports it to auth) | Sign-in with "your session has ended"; the engine pauses | Kept for the same account (M-23) |
| Assignment revoked | 404 for the session (or its assignment) | "This questionnaire is no longer available to you"; unsent answers on this phone are removed, and the screen says so. An assignment that is `cancelled` or `expired` says so on the introduction; whether a session in progress still takes answers is the server's decision | Removed, with notice |
| Version unavailable | 404 `questionnaires.version_not_found`, version ID mismatch, `sessions.version_not_published`, unsupported item type | "This questionnaire version isn't available" or "needs a newer app" | Kept |
| Invalid answer | Zod check against the version | The question is marked "choose again" | That draft removed, with notice |
| Server validation | 400 on save (`sessions.item_not_in_*`, `option_not_in_item`) | Translated problem code; the questions in the refused request are marked "choose again" | Those drafts removed, with notice |
| Already submitted | Status `submitted`/`scored`; `already_submitted`; 409 `sessions.not_in_progress` on save | "Already submitted", with a link to the result; unsent changes are listed as not included | Removed, with notice |
| Time to answer passed | 409 `assignments.assignment_expired` on save or submit (also after maintenance abandons the session) | "This questionnaire was closed: the time to answer has passed"; no more answering or submitting; a queued submission stops with that reason, never retried | Removed, with notice |
| Scoring unavailable | Result `pending`; a 5xx or timeout on the result | "Submitted. The result isn't available yet", with "Check again" | Not applicable |
| Retryable server error | 5xx; 409 `persistence.*` | Automatic retries with backoff, then "Try again" | Kept |
| Consent missing | 403 `sessions.participation_consent_required` | A link to consent; no automatic retry | Kept |
| Answers missing | 409 `sessions.required_items_unanswered` | Back to the review, with missing questions marked | Kept |
| Key reused | 409 `requests.idempotency_key_reused` | A new key is made and the submission is sent again once | Kept |

## 7. Accessibility and direction

- The save line and the offline and submission states are polite live regions; refusals are announced.
- Questions keep the header role, and focus moves to each new question.
- Conflict choices are buttons that name both answers.
- Layout uses start and end only, chevrons mirror, digits are Western.
- Tests render the flow in Arabic with right-to-left layout.

## 8. Testing

Jest with React Native Testing Library. Drafts and migrations run on real SQLite (`node:sqlite`), and the cipher
on Node's AES-GCM.

- **Protection:** the envelope, binding, tampering, a lost key, crypto-shredding, the self-test.
- **Store contract:** encrypted SQLite and memory stores; restart recovery on the same database.
- **Retry policy:** bounds, jitter, attempt cap.
- **AnswerSync:**
  - debounce and deduplication;
  - one save in flight;
  - backoff, offline and back online;
  - conflicts and both resolutions;
  - re-basing after this phone's own saves;
  - local validation;
  - permanent refusals;
  - the last-saved time.
- **SubmissionQueue:**
  - offline submission, then reconnecting;
  - a restart with a queued submission;
  - double taps and lost answers (one server submission, the same key);
  - every outcome in section 6.
- **Screens:**
  - the renderer from the schema (single choice, an unsupported type, optional and required);
  - navigation and jumping to unanswered questions;
  - the introduction;
  - review and required validation;
  - submission states;
  - the offline copy;
  - results under both disclosure settings (the shown values come from the API; no computation);
  - Arabic with right-to-left layout.

## 9. Not built

Multiple-choice, text and numeric questions (M-43). A duration estimate (M-45). Participant-facing values (M-46).
Background sync while the app is closed. Abandoning a session (no endpoint).

## 10. Shared documents

- `docs/decisions.md`:
  - D-70 (M-35 to M-38) answers open question 7 for drafts;
  - D-71 (M-39 to M-42, M-47) covers saving, conflicts, the queue and versions;
  - D-72 (M-43 to M-46) covers question types, the introduction and results;
  - open question 7's status changes.
- `docs/security-model.md`, "Data on a lost phone".
- **Proposed for the backend:**
  - a precondition on `PUT /sessions/{id}/responses` (for example the `lastModifiedAt` the client saw per item)
    to close the window in M-40;
  - truncating `now` to microseconds where answers are stamped, so a save's response and a later read return the
    same `lastModifiedAt`.
