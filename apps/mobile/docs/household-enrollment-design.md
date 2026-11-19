# Household enrollment and assessment discovery: design

**Status:** built on 2026-10-14 on `feat/mobile-household-enrollment` (from `feat/mobile-ui-foundation`), at the owner's
standing request to choose the best option and finish. **Scope:** `apps/mobile` only. Shared documents are not edited
here except the decision records (section 8).

## 1. Understanding

**Asked for:** nine screens (household creation, the household details form, the member roster, add member, edit
member, review, confirm, assessment assignments, household profile), with React Hook Form and Zod, inline and
field-level validation, conditional fields, loading, save and retry states, duplicate-submit protection, an
unsaved-changes warning and a confirmation before removing a member. Confirmation calls the backend; the backend
decides eligibility; the app renders what it returns, including several assignments for one member, none at all,
missing information, network failure and a successful confirmation followed by a failed refresh. Retries must never
create a second household.

**What the platform already defines:**

- `POST /households` (the caller becomes manager, linked to the member marked `isSelf`; one household per account,
  `409 households.already_member`), `POST …/members`, `PATCH …/members/{id}` (merge patch; `{"status": "inactive"}`
  removes), `POST …/confirm-roster` (confirms, then assigns the current wave in one transaction, D-50).
- Roster rules, approved in D-58: one active head and one active main earner, both 18 or over; the manager is 18 or
  over and cannot be removed; at confirmation, a relationship for every member but the head and a work status for
  every adult. Child presence is derived from the roster (D-24). No names or identity numbers are stored (open
  question 19). The relationship and work-status value lists await approval (open question 18).
- Assignments for members with their own account are never visible to the manager (D-49): `GET /assignments` lists
  only the members the caller may act for.

## 2. The fields, against the requested baseline

| Requested | What the app does | Why |
| --- | --- | --- |
| Household member count | Shown, never asked: the number of active members on the roster | The approved schema derives it from the roster; a typed number could disagree with it |
| Member age | Year and month of birth; the server computes the age | D-24 |
| Employment status | Required for adults, optional under 18 | D-58 (roster completeness) |
| Head of household | A yes/no switch per member, adults only; exactly one is needed to confirm | D-50, D-58 |
| Primary income earner | A separate switch, adults only, at most one, independent of the head | D-58; the two roles may be one person or two |
| Children under 18 | Not asked | Not in the approved schema: derived from the roster (D-24) |
| National identity number | Never asked; nor names | Not approved (open question 19) |

## 3. Screens and routes

| Screen | Route | Feature component |
| --- | --- | --- |
| Household creation | `/household/create` | `HouseholdStart` |
| Household details form ("about you") | `/household/create/about-you` | `AboutYouForm` |
| Member roster | `/household/[householdId]/members` | `RosterScreen` |
| Add member | `/household/[householdId]/members/add` | `AddMemberScreen` |
| Edit member | `/household/[householdId]/members/[memberId]` | `EditMemberScreen` |
| Review roster | `/household/[householdId]/review` | `ReviewRoster` |
| Confirm roster | `/household/[householdId]/confirm` | `ConfirmRoster` |
| Assessment assignments | `/household/[householdId]/assignments` | `HouseholdAssignments` |
| Household profile | `/household/[householdId]` | `HouseholdProfile` |

Routes stay thin. Shared pieces: `MemberFormScreen` and `MemberFields` (the one member form), `MemberCard`,
`RosterSummary`, `memberLabels`, `useRosterData`. Requests live in `rosterRequests.ts` (plain functions over
`ParticipantApi`) and `householdMutations.ts` (TanStack Query hooks); no screen calls the API directly. Everything
else comes from the existing design system; the only change there is an optional `accessibilityLabel` on `AppButton`.

## 4. Decisions

| # | Decision | Why |
| --- | --- | --- |
| M-24 | The household is created with the caller only; everyone else is added one at a time with `POST …/members` | The server is the record from the first step, so nothing about the family is kept on the device (D-16); each person is checked on their own; the same screens serve first enrollment and later changes |
| M-25 | The approved roster rules guide the forms (`rosterRules.ts`): fields and roles shown, missing details listed before confirming. The server checks every rule again; its problem code, translated, always wins | Prevents invalid states without a second authority. This is not eligibility: nothing on the device decides who answers what |
| M-26 | Giving a member a role another member holds first clears it from that member (`PATCH {isHeadOfHousehold:false}`), then sets it. If setting it fails, the role is given back (best effort; refused, rightly, when the failed request did reach the server). The form says whose role is taken over, and asks to check relationships after a new head | The server allows one active holder of each role, checked row by row; a failed move must not leave the household without a head; the head's old relationships are relative to the old head |
| M-27 | Retrying never repeats a change (section 5) | "Do not create duplicate households when retrying an uncertain request" |
| M-28 | Duplicate submits: a single-flight ref per action (two presses in one frame start one request), a busy button while saving, and the form stays locked after a successful save until the screen has gone | A test found a late press after success adding the member twice |
| M-29 | Unsaved changes: a `beforeRemove` listener holds the header back button, the system back button and back gestures and asks to discard or keep editing; the iOS swipe-back gesture is turned off while there are changes | A native gesture cannot be held |
| M-30 | The confirmation's answer is kept in the query cache (`participantKeys.rosterConfirmation`) and shown on the assignments screen even if refreshing anything afterwards fails. It is never fetched (there is no endpoint) and is dropped on any roster change | "Successful confirmation followed by a failed refresh" must still read as a success |
| M-31 | Assignments are grouped by member. One whose details the app cannot read shows its status only: "answered in their own account" for a member with an account, otherwise "details could not be loaded". No control adds, removes or changes an assignment, and no answers are read | D-49; the UI must not let a participant change a backend-derived result |
| M-32 | The review asks the person to confirm "everyone who lives in my home is listed" before continuing. It is neither stored nor sent | Stands in for a household-size question without inventing a field |
| M-33 | The fixture API refuses roster changes with the server's problem codes for its published rules (head, earner, ages, future birth months, the manager, completeness at confirmation) | So every error state can be used in development; its assignments stay scripted |
| M-34 | A confirmed household can confirm again from the review (and the profile offers it) | The server clears a confirmation only when the roster changes; confirming is how a household asks for a newly opened round's questionnaires |

## 5. Safe retries

| Request | Idempotent on the server? | What a retry does |
| --- | --- | --- |
| Create household | No (one per account, else `409 households.already_member`) | After a failure that may have reached the server (no connection, timeout, 5xx), the next attempt reads `GET /me` first and uses the household found. A 409 `already_member` also leads to the existing household, with a notice |
| Add member | No | Each attempt reads the roster first. After an uncertain failure, an active member who was not on the roster before the first attempt is the lost request's: it is patched to the current details instead of adding another |
| Edit member | Yes (merge patch) | The patch is computed against the server's current record each time, so repeating it changes nothing more |
| Remove member | Effectively | After an uncertain failure, `404 households.member_not_found` counts as done |
| Confirm roster | Yes | Sent again as is: confirming moves the confirmation time and the server keeps the assignments it made for the wave (D-35) |

A refusal (400, 403, 404, 409) is an answer, not uncertainty: it is shown in words with the action that fixes it.

## 6. States

- Loading: `QueryView` per screen; buttons show progress and announce busy.
- Save failure: the translated problem appears beside the save button with "Try again", which sends the same values.
- Missing information at confirmation (`households.roster_has_no_head`, `…roster_missing_relationship`,
  `…roster_missing_employment_status`): "Some details are missing", the server's reason, and a way back to the review.
- No eligible assessments: "No questionnaires this round" when the wave assigned nothing; "No round is open right now"
  when the server found no open wave (`waveId: null`).
- Withdrawn household: read-only everywhere, with a notice.
- Accessibility: every control is named (a test walks every button, switch and radio); member cards are read as one
  sentence with their roles in words; "Edit" and "Remove" carry the member's description so several cards can be told
  apart; errors are announced; 48-point targets from the design system.
- Right to left: start/end styles only (lint), forward chevrons mirror, Arabic list separators, Western digits.

## 7. Tests

Unit: the member schema (inline and cross-field rules, Arabic digits, Arabic messages, patches), the roster rules,
every safe-retry path in `rosterRequests` against the fixture API with scripted faults (`test-utils/faultyApi.ts`:
`fail` never reaches the server; `lose` lets it act and drops the answer), the single-flight hook, the PATCH and
confirm calls of the HTTP API, and the fixture API's roster rules. Screens: adding, editing and removing members,
validation, conditional fields, duplicate-submit prevention, the unsaved-changes question, roster confirmation (success,
missing information, network failure and retry), several assignments for one member and for several members, none
assigned, no open round, a failed refresh after confirming, a member with their own account, no controls that change
assignments, right-to-left rendering in Arabic and accessible names. Screen tests wait for background refetches to
settle (`settleQueries`) so none lands outside `act`.

## 8. For the main implementation agent (shared documents)

Recorded in `docs/decisions.md` as **D-67** (household enrollment: M-24 to M-26, M-32, M-34), **D-68** (safe retries and
duplicate submits: M-27 to M-29) and **D-69** (roster confirmation and assignments: M-30, M-31), with the
foundation's D-63 to D-66.

Backend follow-ups worth considering (not built here):

- `POST /households` and `POST …/members` accept no `Idempotency-Key`. The app reconciles by reading first, which is
  safe while only the manager changes the roster; an `Idempotency-Key` (D-54) would make it exact once joining a
  household exists.
- The confirm-roster response carries codes, not instrument names, so the app joins it with `GET /assignments`.
- Removing the head of household leaves the roster without one until another member is made head (open question 24);
  the app flags it before confirmation.

Edits to make: `CONTRIBUTING.md` repository map needs nothing new; `docs/development-plan.md` can record that the mobile
enrollment flow exists against the participant API and development fixtures.
