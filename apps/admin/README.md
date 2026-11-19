# Azwah admin dashboard

The researcher and administrator dashboard: Next.js 16.4 (App Router, no Cache Components), React 19.3, TypeScript
strict, Tailwind CSS 4, shadcn/ui (base-nova), TanStack Query, Recharts, Zod, `openid-client` and `jose`.
Next.js 16 differs from older documentation: read the matching guide in `node_modules/next/dist/docs/` before writing
Next.js code.

## Quick start (mock data, no API needed)

    npm ci
    cp .env.example .env.local     # development sign-in + mock data
    npm run dev                    # http://localhost:3000, pick a persona on the sign-in page

Every page then says "Development mock data: every number on these pages is synthetic".

**Open it at the configured address.** `ADMIN_APP_ORIGIN` must equal the exact origin in the browser's address bar
(scheme, host and port). It defaults to `http://localhost:3000` in development. If port 3000 is taken, `next dev`
starts on 3001 and says so: then set `ADMIN_APP_ORIGIN=http://localhost:3001` in `.env.local` and restart, or free
port 3000. The same goes for `127.0.0.1`, a LAN address or a tunnel. On any other address sign-in is sent to the
configured one, and development sign-in, sign-out, export requests and closing a privacy request are refused as
cross-origin (the sign-in page and the forms say why). That check is the CSRF protection; it is not relaxed in
development.

### For a demonstration

    ADMIN_ENVIRONMENT=development
    ADMIN_AUTH_MODE=development
    ADMIN_DATA_SOURCE=mock
    ADMIN_MOCK_SCENARIO=approved_scores
    # ADMIN_APP_ORIGIN=<the exact address you will open, if not http://localhost:3000>

`approved_scores` fills in the score charts, the wave comparison and the household results. The default,
`no_approved_scores`, shows the platform's real state (no approved scoring rule set, so no scores) over the same
households and assignments. Restart the server after changing either setting.

The mock is one synthetic data set (`src/lib/mock/fixtures.ts`): households, members, assignments, sessions and score
results generated from a fixed seed, so it is the same on every run. Every number on every page is counted from it
(`src/lib/mock/aggregates.ts`) with the API's definitions and its small-cell suppression, so the pages agree with each
other; `src/lib/mock/consistency.test.ts` checks that. The questionnaires carry the platform's codes and shape
(`household_assessment`, 39 items; `abu_dhabi_family_survey`, modules A, B and C) with placeholder text labelled
`[DEV SAMPLE]`, never the real wording. Only the household assessment is scored, with the bands of D-103 read from each
synthetic total (39 to 78 vulnerable, 79 to 117 transitional, 118 to 156 flourishing); every result is complete, as
its rule set allows no other, and the modules have no scores, because no scoring is approved for them. The mock's
export catalog and files carry exactly the columns of the API's `ExportCatalog` (the consistency test reads it).

Checks: `npm run typecheck`, `npm run lint`, `npm test`, `npm run build`. Run `npm run build` once after deleting a
route: `tsc` reads the route types Next.js generated in `.next/types`.

## In Docker Compose

`npm run infra:up` (the root README's "Quickstart") builds this app's image (`Dockerfile`: `npm ci`, `npm run build`,
`next start` as the `node` user) and runs it at `http://localhost:3000` (`ADMIN_PORT`) against the Compose API, with
OIDC sign-in through the Compose identity provider's simulated staff personas. The settings are in the `admin` service
of `infra/docker-compose.yml`; `ADMIN_SESSION_SECRET` and `IDENTITY_PROVIDER_ADMIN_CLIENT_SECRET` come from the root
`.env`. The issuer is `http://localhost:5090` for the browser and the dashboard's server alike: the `admin-network`
service holds the dashboard's network namespace and forwards that port to the identity provider (D-150).

## Who sees what

Sections come from `GET /api/v1/me/permissions`; every page checks its own permission on the server, and the API
authorises every request again. Hiding a link is never the control.

| Role | Sections |
| --- | --- |
| Researcher | Overview, Resilience analytics, Research exports |
| Program administrator | Overview, Assessment monitoring, Participation, Privacy requests |
| System administrator | Questionnaires and rules (read-only), Household results, Safety alerts |
| Support coordinator | None yet (welcome page); the referral queue is not part of the dashboard |

Everyone signed in also sees System status.

Privacy requests (`/privacy-requests`, permission `privacy.requests.manage`) lists families' access, correction and
erasure requests, open (oldest first) or closed, and closes one with an outcome (completed, declined or withdrawn) after
an explicit confirmation. Closing records the outcome only: the request itself is handled through the agreed
procedure, and erasure is the operator command, never a dashboard action.

Safety alerts (`/safety-alerts`, permission `safety_alerts.read`) lists signs that a participant may be in danger and
families whose score is in the lowest band, as codes only: an alert holds no conversation text. The default filter is
the alerts that need attention (open and acknowledged); filters live in the URL, and the list is read again every 30
seconds. An administrator acknowledges an alert or resolves it with an outcome; the dashboard decides nothing else. The
navigation shows a badge with the count that needs attention (red when an open alert is critical), and a shell watcher
shows a "New safety alert" toast when a later poll reports a newer unresolved alert (never on the first load). The
platform holds no contact details for families; the page's guidance points staff to their safeguarding procedure and 999
for immediate danger. Signal codes get English and Arabic names; an unknown code is shown as recorded.

Household results (`/results`, permission `results.read`, system administrators only) lists each scored session's
latest recorded result from `GET /api/v1/admin/results`: household reference, wave, questionnaire, state, total and
band, newest first, filtered by wave, questionnaire code or an exact household reference (kept out of the URL). Totals
and bands are shown exactly as the platform recorded them, never calculated or rounded here; the bands `vulnerable`,
`transitional` and `flourishing` get their names, any other code appears as it is. The platform audits every read.

## How it talks to the API

The browser never calls the API. It calls this app's backend-for-frontend, `/bff/...`
(`src/app/bff/[...path]/route.ts`), which forwards an allowlist of routes (`src/lib/server/bff-routes.ts`) with the
session's bearer token, keeps the API's status and body, and sets `Cache-Control: no-store`. Every response is
validated with Zod (`src/lib/api/schemas.ts`) before a page uses it. Nothing about requests, responses or tokens is
logged.

| Path | What it does |
| --- | --- |
| `src/proxy.ts` | Keeps pages and `/bff/*` behind a session; refreshes OIDC tokens a minute before they expire |
| `src/app/(dashboard)/layout.tsx` | Loads the caller's permissions on the server and builds the navigation |
| `src/app/auth/*` | OIDC sign-in, callback and sign-out; development sign-in |
| `src/lib/server/*` | Server-only: configuration, session sealing, OIDC, BFF, caller, guards |
| `src/lib/api/*` | Client: Zod schemas, the BFF client, TanStack Query hooks |
| `src/lib/mock/*` | Server-only development mock of the API's admin routes |
| `src/features/*` | One folder per page |
| `src/components/{app-shell,states,data}` | Shell, empty/error/denied states, badges, pagination, chart figure |

## Configuration

Every variable is server-only (none starts with `NEXT_PUBLIC_`). [`.env.example`](.env.example) lists them all.

| Variable | Meaning |
| --- | --- |
| `ADMIN_ENVIRONMENT` | `development` enables development sign-in and mock data. Anything else, or nothing, is production |
| `ADMIN_APP_ORIGIN` | This app's origin, for sign-in redirects and CSRF checks. **Must be the exact address the dashboard is opened at** (default `http://localhost:3000` in development; see Quick start) |
| `ADMIN_SESSION_SECRET` | Base64 of at least 32 random bytes; required for OIDC and in production |
| `ADMIN_SESSION_MAX_AGE_SECONDS` | Absolute session lifetime, 300 to 86 400 (default 8 hours) |
| `ADMIN_AUTH_MODE` | `oidc` (default in production) or `development` |
| `OIDC_ISSUER`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`, `OIDC_SCOPE`, `OIDC_RESOURCE` | The identity provider; HTTPS outside development |
| `ADMIN_DATA_SOURCE` | `api` (default) or `mock` (development sign-in only) |
| `ADMIN_API_URL` | The API's address when the data source is `api` |
| `ADMIN_DEV_TOKEN_<ROLE>` | Development only: a bearer token per persona for the real API |
| `ADMIN_MOCK_SCENARIO` | `no_approved_scores` (default) or `approved_scores` (the one for a demonstration: synthetic scores are shown) |

A setting that does not fit (for example development sign-in outside development) stops sign-in and the sign-in page
lists what to fix.

### Against the real API in development

The dashboard calls the API from its own server, so the API needs no CORS entry for it. Which sign-in works depends on
how the API was started (see [`backend/README.md`](../../backend/README.md)):

| API started with | Address | Sign in to the dashboard with |
| --- | --- | --- |
| `dotnet run` in `backend/FamilyResilience.Api` | `http://localhost:5080` | Development sign-in and `dotnet user-jwts` tokens (below), or OIDC once the API trusts the identity provider |
| Docker Compose (`npm run infra:up`) | `http://localhost:8080` | OIDC against the Compose identity provider only (see OIDC below); the Compose stack already runs this dashboard ("In Docker Compose") |

`dotnet user-jwts` tokens are signed with a key in your user secrets. Only the API started with `dotnet run` has that
key: the Compose API on port 8080 refuses them (401, shown as "Your session has ended"). So for development sign-in
against the real API, start it with `dotnet run` and mint one token per persona:

    cd backend/FamilyResilience.Api
    dotnet user-jwts create --role researcher             # copy the token into ADMIN_DEV_TOKEN_RESEARCHER
    dotnet user-jwts create --role program_administrator  # ADMIN_DEV_TOKEN_PROGRAM_ADMINISTRATOR
    dotnet user-jwts create --role system_administrator   # ADMIN_DEV_TOKEN_SYSTEM_ADMINISTRATOR

Set `ADMIN_DATA_SOURCE=api` and `ADMIN_API_URL=http://localhost:5080`. A persona without a token shows "Development
token not set".

### OIDC

`ADMIN_AUTH_MODE=oidc` uses the authorization code flow with PKCE (S256), `state` and `nonce`. Register
`<ADMIN_APP_ORIGIN>/auth/callback` as the redirect URI and `<ADMIN_APP_ORIGIN>/sign-in?signedOut=1` as the
post-logout redirect URI. Ask for `offline_access` (the default scope) so the server can refresh tokens; set
`OIDC_RESOURCE` if the issuer needs a resource indicator to put the API in the token's audience. For real OIDC in
development, use the Azwah identity provider (`backend/FamilyResilience.IdentityProvider`; staff sign in with its
simulated Development personas; settings in `.env.example`). **Production UAE PASS is not onboarded**, and no staff
provider is configured for production: the dashboard never names one.

After sign-in the server records it with the platform (`POST /api/v1/me/sign-ins`, best effort). Sign-out renews an
expired access token, records the end, revokes the refresh token when the issuer supports revocation, clears the
cookie and ends the issuer session (D-84).

## Session and security

- The session is a JWE (`dir`, `A256GCM`) in an HttpOnly, `SameSite=Lax` cookie, `Secure` with the `__Host-`
  prefix over HTTPS, split into chunks when long. It holds the access and refresh tokens (OIDC) or the persona
  (development), never roles. Nothing is kept in `localStorage` or `sessionStorage`.
- Development sign-in without `ADMIN_SESSION_SECRET` uses a key derived from the origin: the proxy runs in its own
  bundle, so a random per-process key would not match. It is safe only because those sessions hold a persona name.
- State-changing requests (sign-out, development sign-in, export requests, closing a privacy request) must come from
  this origin (`Origin` header equal to `ADMIN_APP_ORIGIN`); BFF bodies must be JSON and at most 16 KB. A refused form
  post from a browser is sent to the sign-in page, which explains it (anyone can open that page from a link, so it
  names `ADMIN_APP_ORIGIN` only when `ADMIN_ENVIRONMENT=development`); a refused BFF request is explained in the form
  that sent it. Nothing is changed in either case, and the check is the same in development.
- Token refresh is de-duplicated per refresh token inside one server process. **Running several instances needs a
  shared session store** (or sticky sessions), because an issuer that rotates refresh tokens would otherwise see
  reuse. Security headers come from `next.config.ts`; HSTS is left to the HTTPS front end.

## Display rules

The dashboard formats the API's numbers; it does not compute statistics, scores, eligibility or suppression. The API
hides a cell that covers fewer than N people (N is the response's `minimumGroupSize`; people are the distinct members
behind a count of assignments or results, D-58) and, when only one cell of a group would be hidden, the cell covering
the next fewest people too. So a hidden count is not always a small one: such cells show "Hidden", never "fewer than
N", and the note above each table says why. As a safeguard, an unflagged count from 1 to N − 1 is hidden too.
Versions, modules and rule sets are never combined. Study cohorts do not exist in the platform, so pages filter by
wave only (the overview and the analytics say so under the wave filter). Dates are shown in UTC with Western digits
in both languages.

Labels say what the platform counts: "All assignments" includes cancelled ones, "Assigned, not cancelled" is the
denominator of every completion rate, "Active family members" is every active roster member of any age in the
households taking part (members of withdrawn households are left out, though the participation list still shows
them), and a score distribution counts complete results with a band (one per scored session), not people. Bands the
specification names (`vulnerable`, `transitional`, `flourishing`, D-103) are named in the reader's language and
listed low to high on both the analytics and the household results (`src/lib/bands.ts`); any other code appears as
recorded. Counted nouns use the language's plural forms (`src/lib/plural.ts`).

## Language

The language switch stores `lang` in a cookie and sets `lang` and `dir` on `<html>`. Messages live in
`src/lib/messages/{en,ar}.ts`; the type and `src/lib/i18n.test.ts` keep their keys identical and the Arabic in Arabic
script. Use logical Tailwind utilities (`ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`, `text-start`) so pages mirror.
Charts are the exception: `ChartFigure` draws them with `dir="ltr"` (under `rtl`, SVG text anchors swap and axis
labels land on the bars) and they mirror by reversing their axes; tooltips set the page direction on themselves
again. The browser tab's title follows the language too (`generateMetadata` in `src/app/layout.tsx`).
