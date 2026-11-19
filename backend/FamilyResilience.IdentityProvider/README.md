# Azwah identity provider

`FamilyResilience.IdentityProvider` is the platform's OpenID Connect provider. It signs people in with **UAE PASS**
(the UAE PASS *staging* environment and its public sandbox client, until the entity's production onboarding), and
issues the tokens the mobile app, the admin dashboard and the API use (D-129). It can also sign people in with a
**Google account** once you give it your own Google OAuth client (D-147, below). It is a separate app with no project
references: the API never references it and the API's Docker image never contains it.

It runs in one of two modes, decided by the hosting environment alone:

| | `Development` | Deployed (any other environment name) |
| --- | --- | --- |
| Sign-in | UAE PASS staging (when the sandbox client is set) **and** simulated test personas: whoever opens the persona page picks one, no identity is checked | UAE PASS staging only. There is no persona page; a persona choice is refused |
| Issuer | `http` or `https` | `https` only |
| Signing key | `IdentityProvider:SigningKey:Pem` if set, otherwise a random RSA-2048 key per process | `IdentityProvider:SigningKey:Pem` required |
| Clients | From `appsettings.Development.json` | From configuration; `https` or app-scheme redirect URIs only, no Expo Go addresses, every confidential client has a secret |
| Discovery and tokens | Carry `"azwah_development_only": true` (release builds of the app refuse such an issuer; a deployed API refuses such tokens) | Carry no development marker; discovery endpoint URLs come from the issuer |
| Pages | Every page says it is simulated | Neutral pages, no persona list |

Start-up refuses a deployed provider without UAE PASS enabled and its credentials, an `https` issuer, a signing key,
or with a plain-`http` redirect URI, an Expo Go address or a confidential client without a secret. It also refuses the
`Production` environment name while only UAE PASS staging can be configured (test identities must not become
production participants); deploy as `Staging`. The error names the
setting, never a value. **Production UAE PASS (`id.uaepass.ae`) is refused in every environment** until onboarding
(open question 5): staging identities are test accounts, production identities are real people.

## Run it

```sh
npm run identity:dev                                   # from the repository root
cd backend && dotnet run --project FamilyResilience.IdentityProvider   # the same thing
```

It listens on `http://localhost:5090` only (launch profile `http`, environment Development), because in Development
anyone who reaches it can mint any persona's token for the local API. Open http://localhost:5090 for the persona
list. In Docker Compose it is the `identity-provider` service (`npm run infra:up`).

The admin client's secret has **no default**. For `dotnet run`, set one in user secrets:

```sh
cd backend/FamilyResilience.IdentityProvider
dotnet user-secrets set "IdentityProvider:Clients:azwah-admin:Secret" "<a development-only value>"
```

Compose takes it from `IDENTITY_PROVIDER_ADMIN_CLIENT_SECRET` in `.env`. While it is missing, every token and revocation
request from `azwah-admin` fails with `invalid_client` (the provider logs a warning at start-up).

## UAE PASS staging

UAE PASS (docs.uaepass.ae) publishes a sandbox client for staging proofs of concept: client ID and secret both
`sandbox_stage`, any redirect URI. **No configuration value in the repository sets them**: set them yourself.

```sh
cd backend/FamilyResilience.IdentityProvider
dotnet user-secrets set "IdentityProvider:UaePass:Enabled" "true"
dotnet user-secrets set "IdentityProvider:UaePass:ClientId" "sandbox_stage"
dotnet user-secrets set "IdentityProvider:UaePass:ClientSecret" "sandbox_stage"
```

Or environment variables (`IdentityProvider__UaePass__Enabled=true`, `IdentityProvider__UaePass__ClientId`,
`IdentityProvider__UaePass__ClientSecret`); Compose takes `IDENTITY_PROVIDER_UAEPASS_ENABLED` (default `true`),
`IDENTITY_PROVIDER_UAEPASS_CLIENT_ID` and `IDENTITY_PROVIDER_UAEPASS_CLIENT_SECRET` from `.env`. In Development, enabled
without a client ID or secret logs a warning and leaves UAE PASS off, so a fresh checkout still starts with personas;
deployed, it is a start-up refusal. The start-up log says when UAE PASS is on and names the callback; it never logs
the secret.

| Setting (`IdentityProvider:UaePass:`) | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` (`true` in `.env.example`) | Turns the UAE PASS sign-in on |
| `ClientId`, `ClientSecret` | none | Required; HTTP Basic client authentication at the token endpoint |
| `AuthorizationEndpoint` | `https://stg-id.uaepass.ae/idshub/authorize` | Must stay on `stg-id.uaepass.ae` (https, default port) |
| `TokenEndpoint` | `https://stg-id.uaepass.ae/idshub/token` | Same rule |
| `UserInfoEndpoint` | `https://stg-id.uaepass.ae/idshub/userinfo` | Same rule |
| `LogoutEndpoint` | `https://stg-id.uaepass.ae/idshub/logout` | Same rule; used by the sign-out chain below |
| `Scope` | `urn:uae:digitalid:profile:general` | |
| `AcrValues` | `urn:safelayer:tws:policies:authentication:level:low` | The web level: the person enters their Emirates ID, e-mail or mobile and approves a matching code in the UAE PASS app |

The callback is fixed: `{Issuer}/uaepass/callback` (`http://localhost:5090/uaepass/callback` by default; it is not a
setting, so the route is known at start-up). The sandbox client accepts any redirect URI.

### How a sign-in works

1. The client sends its normal authorization request. In Development it adds **`idp_hint=uae_pass`** (and optionally
   `ui_locales=ar` or `en`), or the person presses "Continue with UAE PASS (staging)" on the persona page; deployed,
   every valid request goes to UAE PASS. The request is validated exactly as before (client, redirect URI, PKCE,
   `state`, `nonce`).
2. The provider keeps the request in memory under a new random `state` (single use, 10 minutes, at most 1000 waiting)
   and redirects to UAE PASS with `response_type=code`, `client_id`, `redirect_uri` (the callback), `scope`, `state`,
   `acr_values` and `ui_locales` (`ar` when the first requested locale is Arabic, otherwise `en`). If UAE PASS is not
   on, the client gets `error=temporarily_unavailable` instead.
3. UAE PASS returns to `/uaepass/callback`. An unknown, expired or reused `state` shows the refusal page and never
   redirects. A UAE PASS error (the person cancelled) returns `error=access_denied` to the client. Otherwise the provider
   exchanges the code (HTTP Basic, 15-second timeout, 64 KiB answers, no redirects, no request logging), reads userinfo
   with UAE PASS's access token and drops both tokens. Any failure returns `error=temporarily_unavailable` with a fixed
   description; the log names a reason code only.
4. The client receives a normal single-use `code`, its own `state` and `iss`, and redeems it as usual.

The subject is **`uaepass-stg|{uuid}`** (UAE PASS's stable `uuid`) and the role is `participant`. The **access token is
unchanged**: no personal claims. The **ID token** (at redemption and on every refresh) adds, when UAE PASS released
them: `name`, `given_name`, `family_name` (English), `gender` (`male`/`female`) and one object claim:

```json
"azwah_profile": {
  "source": "uae_pass_staging",
  "name_ar": "…", "given_name_ar": "…", "family_name_ar": "…",
  "nationality_en": "ARE", "nationality_ar": "…",
  "user_type": "SOP3",
  "emirates_id_masked": "784-••••-•••••42-1",
  "mobile_masked": "+971•••••••42",
  "email_masked": "t•••@example.ae"
}
```

Absent values are omitted; only `uuid` is required. The Emirates ID keeps the `784` prefix, the last two digits of the
seven-digit block and the check digit: the full number never leaves the provider. UAE PASS documents no date of birth
in userinfo, so there is no `birthdate`. Everything stays in memory with the grant and dies with the process.

### How a sign-out works

UAE PASS keeps a web session in the browser, so without a logout the next sign-in on the same phone could skip the
login page. `GET /end-session` therefore accepts **`idp_hint=uae_pass`**: with UAE PASS on, after the usual checks
(registered client, registered post-logout URI), the provider keeps the client's post-logout address and `state` in
memory under a random single-use key (10 minutes, at most 1000 waiting; when the store is full the provider skips UAE
PASS and redirects to the client directly) and sends the browser to UAE PASS logout with
`redirect_uri={Issuer}/uaepass/signed-out?r={key}`. UAE PASS is documented to return there, and the provider then
redirects to the client's post-logout URI with its `state`. The chain is tested against a stub; it has not yet been run
against the real UAE PASS logout endpoint. An unknown, expired or reused key shows the signed-out page and never redirects.
Without the hint, or with UAE PASS off, end-session redirects to the client at once. The mobile app sends the hint at
sign-out for UAE PASS sessions and returns on `azwah://auth/signed-out` (Expo Go: `exp://…/--/auth/signed-out`).

### A staging test account

You cannot finish a staging sign-in without one, and there are no published test accounts or fixed one-time codes:

1. Install the **UAE PASS staging app** on an Android phone or emulator: `https://stg-android.uaepass.ae` serves the
   APK (package `ae.uaepass.mainapp.stg`; the QR code on docs.uaepass.ae, "Staging Apps", points there). iOS: the
   staging build on the same page, then trust "Dubai Smart Government" under Settings › Device Management.
2. Create an account: "Create New Account", accept the terms, "Proceed as Citizen or Resident", then either scan an
   Emirates ID or choose "I don't have an Emirates ID" and type details. Enter the one-time codes sent to your mobile
   number and e-mail address, set a PIN, and choose "Later" when asked to upgrade (staging accounts cannot use the
   in-app upgrades; docs.uaepass.ae describes the staging upgrade to SOP2/SOP3 separately).
3. Sign in from the app, or open `http://localhost:5090/authorize?...&idp_hint=uae_pass`: enter the account's
   Emirates ID, e-mail or mobile on the UAE PASS page, then approve the matching code in the staging app. On the
   emulator, `adb reverse tcp:5090 tcp:5090` lets the browser come back to the provider at `localhost`.

## Google sign-in (D-147)

An optional second upstream, beside UAE PASS: a person signs in with a Google account and becomes a **participant**
(subject `google|{sub}`). It is off until you give the provider your own Google OAuth client. **No configuration value in
the repository sets one.**

1. In Google Cloud Console, open *APIs & Services › Credentials*, create an **OAuth client ID** of type **Web
   application**, and add the authorized redirect URI `{Issuer}/google/callback` (`http://localhost:5090/google/callback`
   by default; Google accepts plain `http` only for `localhost`). Configure the consent screen (the `openid`, `email`
   and `profile` scopes; while it is in *Testing*, add the accounts that may sign in as test users).
2. Give the provider the client:

   ```sh
   cd backend/FamilyResilience.IdentityProvider
   dotnet user-secrets set "IdentityProvider:Google:Enabled" "true"
   dotnet user-secrets set "IdentityProvider:Google:ClientId" "<client ID>.apps.googleusercontent.com"
   dotnet user-secrets set "IdentityProvider:Google:ClientSecret" "<client secret>"
   ```

   Compose takes `IDENTITY_PROVIDER_GOOGLE_ENABLED` (default `true`), `IDENTITY_PROVIDER_GOOGLE_CLIENT_ID` and
   `IDENTITY_PROVIDER_GOOGLE_CLIENT_SECRET` from `.env`. Enabled without an ID or secret, Google stays off with a start-up
   warning (event 3014).

| Setting (`IdentityProvider:Google:`) | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` (`true` in `.env.example`) | Turns the Google sign-in on |
| `ClientId`, `ClientSecret` | none | Required; sent in the form at Google's token endpoint |
| `ApprovalReference` | none | Required outside Development: Google accounts are real people (open question 73). Ignored in Development |
| `AuthorizationEndpoint` | `https://accounts.google.com/o/oauth2/v2/auth` | Must stay https on `accounts.google.com` |
| `TokenEndpoint` | `https://oauth2.googleapis.com/token` | Must stay https on `oauth2.googleapis.com` |
| `UserInfoEndpoint` | `https://openidconnect.googleapis.com/v1/userinfo` | Must stay https on `openidconnect.googleapis.com` |
| `Scope` | `openid email profile` | Must include `openid` |

How it works, next to UAE PASS:

- `GET /authorize?…&idp_hint=google` (or "Continue with Google" on the persona page) validates the client's request as
  always, keeps it under a fresh single-use state with a PKCE verifier (10 minutes, shared with UAE PASS's store, which
  remembers the upstream so neither callback accepts the other's state) and redirects to Google with `response_type=code`,
  `client_id`, `redirect_uri`, `scope`, `state`, `code_challenge` (S256), `prompt=select_account` and `hl` (`ar` or
  `en`). With Google off, the client gets `error=temporarily_unavailable`.
- `GET /google/callback` exchanges the code (client ID, secret and verifier; 15-second timeout, 64 KiB answers, no
  redirects, no request logging), reads userinfo, drops Google's tokens and returns the provider's own single-use code
  to the client. Google's error returns `access_denied`; any other failure `temporarily_unavailable`; the log names a
  fixed reason code only (event 3013).
- The ID token adds `name`, `given_name`, `family_name` and `"azwah_profile": { "source": "google", "email_masked":
  "t•••@example.com" }` (the address only when Google says it is verified). The access token is unchanged.
- Discovery lists the configured upstreams in `azwah_identity_providers` (`["uae_pass", "google"]`); the mobile app shows
  **Continue with Google** only when `google` is listed.
- Sign-out: Google has no end-session for this client, so `GET /end-session` simply returns to the client; the account
  chooser at the next sign-in (`prompt=select_account`) keeps a shared phone from reusing an account silently.
- Deployed, `idp_hint=google` goes to Google and every other request still goes to UAE PASS, which stays required.

### Google staff sign-in (Development only, D-149)

For the shareable demonstration stack (D-148) the admin dashboard can accept Google accounts as **staff**. Two settings,
both off by default, turn it on for one client:

| Setting | Default | Meaning |
| --- | --- | --- |
| `IdentityProvider:Google:StaffRoles` | empty (off) | The roles a staff Google sign-in receives. Each must be `system_administrator`, `program_administrator`, `researcher` or `support_coordinator`; `participant` and anything else are refused at start-up. Several roles work (the dashboard shows the union of their sections) |
| `IdentityProvider:Clients:<id>:GoogleStaffSignIn` | `false` | This client's Google sign-ins become staff. Only a confidential client may opt in (the dashboard's `azwah-admin`, never the phone) |

With both set and Google configured, a Google sign-in for that client gets the subject **`google-staff|{sub}`** and the
listed roles instead of `google|{sub}` and `participant`; the persona page labels its "Continue with Google" with the
roles. The same person's phone sign-in stays `google|{sub}`, a separate platform account, because the API keeps roles per
subject. Persona and UAE PASS sign-ins are unchanged. Start-up logs a warning (event 3015) naming the client and the roles.

**The gate is your Google OAuth client's test users** (APIs & Services › OAuth consent screen › Test users, while the app
is in *Testing*): every account on that list can sign in to the dashboard with these roles. Publishing the consent screen
would let every Google account in. Outside Development, start-up refuses a non-empty `StaffRoles` and any client with
`GoogleStaffSignIn`. Use it with synthetic data only (T-40).

Environment variables (Compose `identity-provider` service, `environment:`):

```yaml
IdentityProvider__Google__StaffRoles__0: system_administrator
IdentityProvider__Google__StaffRoles__1: program_administrator
IdentityProvider__Google__StaffRoles__2: researcher
IdentityProvider__Google__StaffRoles__3: support_coordinator
IdentityProvider__Clients__azwah-admin__GoogleStaffSignIn: "true"
```

Do not add these lines with empty defaults (`${…:-}`): an empty entry is an unknown role and stops start-up. User secrets
for `dotnet run`: `dotnet user-secrets set "IdentityProvider:Google:StaffRoles:0" "system_administrator"` (and so on) and
`dotnet user-secrets set "IdentityProvider:Clients:azwah-admin:GoogleStaffSignIn" "true"`.

## Settings (`IdentityProvider` section)

| Setting | Default | Meaning |
| --- | --- | --- |
| `Issuer` | `http://localhost:5090` | The `issuer` in discovery, `iss` in tokens and authorization responses. It never follows the request's host; deployed it must be `https` |
| `Audience` | `azwah-api` | The access tokens' `aud` |
| `AccessTokenSeconds` | 600 | Access and ID token lifetime |
| `SigningKey:Pem` | none | An unencrypted RSA private key (PKCS#8 or PKCS#1 PEM, at least 2048 bits) from a secret store. Required deployed; optional in Development. Its `kid` is derived from the public key, so it is stable across restarts |
| `Clients:<id>` | see below | `Confidential`, `Secret`, `RedirectUris`, `PostLogoutRedirectUris`, `AllowExpoGoRedirects`, `GoogleStaffSignIn` (D-149, Development only) |
| `UaePass:*` | above | The UAE PASS staging sign-in |
| `Google:*` | above | The Google sign-in (D-147) |

Development clients (`appsettings.Development.json`):

| Client | Type | Redirect URIs | Post-logout redirect URIs |
| --- | --- | --- | --- |
| `azwah-mobile` | public (PKCE only) | `azwah://auth/callback`; Expo Go: any `exp://…/--/auth/callback` | `azwah://auth/signed-out`; `exp://…/--/auth/signed-out` |
| `azwah-admin` | confidential (`client_secret_basic` or `client_secret_post`) | `http://localhost:3000/auth/callback` | `http://localhost:3000/sign-in?signedOut=1` |

Redirect URIs match exactly. Expo Go addresses must be printable ASCII without user info, query or fragment. A
deployed provider registers its own clients in configuration (for example `IdentityProvider__Clients__azwah-mobile__RedirectUris__0=azwah://auth/callback`,
`…__PostLogoutRedirectUris__0=azwah://auth/signed-out`, `…__AllowExpoGoRedirects=false`); nothing from
`appsettings.Development.json` is loaded there.

## Personas (Development only)

| Code | Subject | Roles |
| --- | --- | --- |
| `participant_lead` | `dev-participant-lead` | participant |
| `participant_member` | `dev-participant-member` | participant |
| `participant_new` | `dev-participant-new` | participant |
| `researcher` | `dev-researcher` | researcher |
| `program_administrator` | `dev-program-administrator` | program_administrator |
| `system_administrator` | `dev-system-administrator` | system_administrator |
| `support_coordinator` | `dev-support-coordinator` | support_coordinator |

Subjects are stable, so platform records made with a persona survive restarts. Tokens carry no names or other
personal claims. UAE PASS gives the `participant` role only; staff roles exist in Development through personas (and
Google staff sign-in when configured, D-149), and a deployed staff sign-in is still open (open question 37).

## Protocol

| Endpoint | Behaviour |
| --- | --- |
| `GET /.well-known/openid-configuration` | Discovery. In Development, endpoint URLs use the request's scheme and host (the API in Compose fetches keys from `http://identity-provider:5090`); deployed they come from the issuer. `issuer` is always the configured value |
| `GET /jwks` | One RS256 key: the configured PEM, or a random RSA-2048 key made when the process starts (restarting then invalidates every token) |
| `GET /authorize` | Authorization code flow only. Requires a registered `client_id` and exact `redirect_uri` (otherwise an error page, never a redirect), `response_type=code`, `scope` with `openid`, `state`, `nonce` and an S256 `code_challenge`. Other problems redirect with `error`, `state` and `iss`. `prompt=none` answers `login_required` (there is no session). Development without `idp_hint`: the persona page; otherwise: UAE PASS |
| `POST /authorize` | Development: checks the request again, then redirects with a single-use 60-second `code`, `state` and `iss` (RFC 9207); Cancel redirects with `error=access_denied`. Deployed: a persona choice is `invalid_request` |
| `GET /uaepass/callback` | Where UAE PASS staging returns the browser after sign-in |
| `GET /uaepass/signed-out` | Where UAE PASS staging returns the browser after logout |
| `GET /google/callback` | Where Google returns the browser after sign-in (D-147) |
| `POST /token` | `authorization_code` (PKCE verifier, same client and redirect URI; a reused code revokes the refresh tokens it issued) and `refresh_token` (rotated on every use; reusing a rotated token revokes the whole family). Refresh tokens are opaque, in memory, and end 8 hours after sign-in. A refresh also returns a new ID token, without `nonce` and with the original `auth_time` (OIDC Core 12.2), so an app that keeps only its refresh token across restarts gets the sign-in's profile back |
| `POST /revoke` | RFC 7009. 200 for any token, known or not; a refresh token ends its family. Access tokens are self-contained and simply expire. Client authentication still applies |
| `GET /end-session` | Redirects (with `state`) only to a post-logout URI registered for the client named by `client_id` or by an `id_token_hint` this provider signed; otherwise shows a page. With `idp_hint=uae_pass`, through UAE PASS logout first (above) |

The access token is a JWT with header `typ: at+jwt`, RS256, and claims `iss`, `sub`, `aud`, `exp`, `iat`, `nbf`,
`jti`, `client_id`, `scope` and `role` (an array). The ID token has `iss`, `sub`, `aud` (the client), `azp`, `exp`,
`iat`, `auth_time` and `nonce` (no `nonce` after a refresh); a UAE PASS sign-in adds its masked profile (above). In
Development both carry `"azwah_development_only": true`. Only `openid` and `offline_access` are granted; a refresh
token needs `offline_access`.

Grants (codes, refresh tokens, pending sign-ins and sign-outs) live in memory: one instance, and a restart ends every
session (the app treats that as an expired session and asks to sign in again). Persistence for more than one instance
is open question 55.

Every response sends `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and
`Referrer-Policy: no-referrer`. Pages send `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline';
form-action 'self'; frame-ancestors 'none'`, except that the persona page also allows its validated redirect target in
`form-action` (its origin, or `azwah:`/`exp:` for app links). "Continue with UAE PASS (staging)" on that page is a
link to `GET /authorize` with `idp_hint=uae_pass`, not a submit button: browsers apply `form-action` to every
redirect that follows a form post, and UAE PASS redirects on between hosts of its own (`stg-id` to `stg-ids`), so a
button was silently blocked in Chrome. Every reflected value is HTML-encoded. The log names personas, clients and fixed reason codes only, never tokens, codes,
secrets, query strings or anything UAE PASS returned.

## Point the API at it

**Docker Compose** does this already: the `api` service sets `Authentication__Schemes__Bearer__Authority=http://identity-provider:5090`,
`RequireHttpsMetadata=false` and audience `azwah-api`.

**`dotnet run`** users opt in through the API's user secrets, so `dotnet user-jwts` keeps working without the provider:

```sh
cd backend/FamilyResilience.Api
dotnet user-secrets set "Authentication:Schemes:Bearer:Authority" "http://localhost:5090"
dotnet user-secrets set "Authentication:Schemes:Bearer:RequireHttpsMetadata" "false"
dotnet user-secrets set "Authentication:Schemes:Bearer:ValidAudiences:1" "azwah-api"
```

Index 1 keeps the `dotnet user-jwts` audience at index 0 (`appsettings.Development.json`). While the authority is set,
the API needs the provider running to fetch its keys; remove the three secrets (`dotnet user-secrets remove …`) to go
back to `dotnet user-jwts` alone. A deployed API needs an `https` authority (D-76), which a deployed provider has.

## Point the admin dashboard at it

In `apps/admin/.env.local`:

```sh
ADMIN_ENVIRONMENT=development
ADMIN_AUTH_MODE=oidc
OIDC_ISSUER=http://localhost:5090
OIDC_CLIENT_ID=azwah-admin
OIDC_CLIENT_SECRET=<the value of IdentityProvider:Clients:azwah-admin:Secret or IDENTITY_PROVIDER_ADMIN_CLIENT_SECRET>
ADMIN_SESSION_SECRET=<base64 of 32 random bytes; required for OIDC>
ADMIN_DATA_SOURCE=api
ADMIN_API_URL=http://localhost:8080
```

`OIDC_SCOPE` defaults to `openid offline_access`, which is what the provider grants. Plain `http` issuers are accepted
only because `ADMIN_ENVIRONMENT=development`. Staff sign in with the Development personas, or with Google when Google
staff sign-in is on (D-149); UAE PASS gives participants only.

## Point the mobile app at it

In `apps/mobile/.env`: `EXPO_PUBLIC_DEV_FIXTURES=false`, `EXPO_PUBLIC_OIDC_ISSUER=http://localhost:5090`,
`EXPO_PUBLIC_OIDC_CLIENT_ID=azwah-mobile` (a public client: there is no secret variable) and
`EXPO_PUBLIC_OIDC_IDP_HINT=uae_pass`, so "Sign in with UAE PASS" goes straight to UAE PASS. Development builds use
`azwah://auth/callback`; Expo Go uses its `exp://…/--/auth/callback` address, which the provider accepts for
`azwah-mobile` in Development.

### Phones and emulators

The issuer must be reachable under **the same URL** from every client: the phone's browser, the app, the admin
server and the API all compare it exactly. Either:

- **Android emulator or USB phone**: forward the ports, so `localhost` on the phone reaches this computer, and keep the
  issuer `http://localhost:5090`:

  ```sh
  adb reverse tcp:5090 tcp:5090
  adb reverse tcp:8080 tcp:8080   # the API (use your API_PORT, or 5080 for dotnet run)
  ```

- **Phone on the same Wi-Fi** (or iOS): opt in to listening on the network, and set the issuer to the computer's LAN
  address for every client, for example `IdentityProvider:Issuer` (or `IDENTITY_PROVIDER_ISSUER`) `http://192.168.1.20:5090`,
  `EXPO_PUBLIC_OIDC_ISSUER` and `OIDC_ISSUER` to the same value. With Compose set `IDENTITY_PROVIDER_BIND=0.0.0.0`; with
  `dotnet run` add `--urls http://0.0.0.0:5090`. Everyone on that network can then sign in as any persona, so do this
  only on a trusted network and switch it back afterwards.

If you change `IDENTITY_PROVIDER_PORT`, change `IDENTITY_PROVIDER_ISSUER` to match.

## Tests

`backend/FamilyResilience.IntegrationTests/IdentityProvider/` runs the provider on a test server: both modes' start-up
rules, discovery and keys (including a configured key's stable `kid`), request validation, the persona page, the code
flow with PKCE, client authentication, refresh rotation and reuse, revocation, end-session and the UAE PASS sign-out
chain, token contents, the UAE PASS staging sign-in against a stubbed UAE PASS and the Google sign-in against a stubbed
Google (no test calls either real service), the
deployed mode (no personas, no development markers, issuer-derived discovery), and end-to-end tests in which the API's
own JWT bearer handler accepts a token the provider issued in either mode (`GET /api/v1/me` shows the subject and
roles).
