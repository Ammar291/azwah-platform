# Threat model

Status: engineering threat model, written with the security hardening of 2026-10-16 (D-76 to D-85); T-19 to T-22
were added with the support-resource directory (D-92 to D-95); T-23 was revised and T-29 added with the Azwah identity
provider (D-129); T-33 was added with the safety alerts (D-135 to D-138), T-34 with the demonstration database
(D-140), T-35 with the phone's production look (D-142), T-36 with calls that carry on through OpenAI's filter
(D-141), T-37 with the demonstration's new check-in (D-144) and T-40 with Google staff sign-in on the demonstration
stack (D-149). It has not been reviewed by an independent assessor.
Nothing here means the platform is security-certified or legally compliant; that needs the reviews listed under
[Before production](#before-production).

## 1. Scope

The participant mobile app, the admin dashboard (a backend-for-frontend, BFF), the API and its PostgreSQL database,
export files, and the Azwah identity provider (D-129). Hosting is not chosen yet (open question 6), so threats to the
hosting platform itself are listed as requirements, not analysed. The support-resource directory is part of the API;
its language-model path has no provider (D-94), so a provider is a future boundary, analysed only as far as T-20 to
T-22 need.

## 2. Assets

| Asset | Class ([security-model.md](security-model.md#data-classification)) | Where it lives |
| --- | --- | --- |
| Assessment answers, scores, tiers, support recommendations and referrals | Sensitive participant data | `assessment_responses`, `score_results`, `support_recommendations`, `support_referrals`; encrypted answer drafts on phones |
| Household roster (year and month of birth, relationship, work status, roles) | Sensitive participant data | `households`, `family_members` |
| Consent records | Sensitive participant data | `consent_records` |
| Research exports | Confidential (pseudonymised research data) | Export files (development storage only today) |
| Tokens (access, refresh, ID) | Confidential (credentials) | Phone keystore (refresh token only), admin session cookie (sealed), memory |
| Safety alerts (that a person may be at risk: codes, times, household reference; no text) | Sensitive participant data | `safety_alerts` |
| Audit trail | Internal | `audit_events` |
| Approved questionnaire content and rule sets | Internal (integrity matters most) | `instrument_versions`, `rule_sets` |
| Approved support-resource directory | Public (integrity matters most); drafts Internal | `support_resources` |
| Support-resource search text | Sensitive participant data, in transit only | Request bodies; never stored |
| Signing keys, client secrets, connection strings | Confidential (secrets) | Secret store, user secrets, `.env` (never git) |

## 3. Actors

| Actor | Trust |
| --- | --- |
| Participant (household manager or member with their own account) | Trusted for their own household's records only |
| Another participant | Untrusted towards this household |
| Researcher, program administrator, support coordinator, system administrator | Trusted for their role's operations only; may be curious or malicious |
| Identity-provider administrator | Can mint or change roles: trusted, but watched (D-81) |
| Database or hosting operator | Trusted infrastructure; can bypass application controls |
| Anonymous internet client | Untrusted |
| Someone holding a participant's phone | Untrusted unless they can unlock it |

## 4. Trust boundaries

1. Phone ↔ API and admin browser ↔ admin server: TLS (deployed), bearer token or HttpOnly session cookie.
2. Admin server ↔ API: bearer token added by the BFF; allowlisted routes only.
3. Clients ↔ identity provider: system browser (mobile) or server-side code flow (admin), PKCE, `state`, `nonce`.
4. API ↔ PostgreSQL: one connection string, TLS required in deployed hosts (D-76).
5. API ↔ export file store: development folder only; production storage not approved (open question 6).
6. API ↔ language-model provider for support-resource discovery: does not exist. No adapter ships, and the API refuses to
   start with the model switched on without one (D-94).
7. API ↔ OpenAI for the assistant and the consultant (D-119, D-120): HTTPS and a server-side WebSocket with the key in a
   header; only the server holds the key and the family context. A deployed API refuses a key without an approval
   reference (open question 52).

## 5. Threats

Residual risk is rated **Low**, **Medium** or **High** after the listed mitigations.

### T-1 Unauthorized household access

- **Scenario:** a participant (or anyone with a valid token) requests another household's household, member,
  assignment or session by ID, or guesses IDs.
- **Mitigations:**
  - Deny by default: a fallback policy needs a signed-in user. `DenyByDefaultTests` sweeps every route the API maps:
    each names a policy or is a listed exception, refuses anonymous callers, and refuses the wrong roles.
  - Resource requirements (`HouseholdManagerRequirement`, `AssignmentParticipantRequirement`,
    `SessionParticipantRequirement`) are decided from the platform's own records (`household_memberships`), never from
    IDs or roles in the request (D-47, D-48), and use cases check again.
  - A refused resource answers 404 exactly like a missing one, so existence never leaks. UUID v7 IDs are not
    sequential within a household.
  - Every refusal of a signed-in caller is audited as `authorization.resource.denied` with the resource ID, so
    enumeration leaves a trail (D-81). Rate limits slow it (D-79).
  - `CrossHouseholdSweepTests` tries every household, member, assignment and session route with a victim's real IDs
    and checks 404, no change and an audited denial.
- **Residual: Low.** Probing is detected but not blocked automatically. Refusals are counted
  (`azwah.authorization.denied`) and an alert rule fires on bursts (D-90); it pages no one until the hosting platform
  connects Alertmanager to a receiver.

### T-2 Cross-member response disclosure

- **Scenario:** one person in a household reads another member's answers, results, consent or history.
- **Mitigations:** `Household.MayActFor` (D-49): a member with their own account is reachable only by themselves,
  never by the manager; the manager acts only for members without an account. Lists (`/assignments`,
  `/me/consents`, `/me/assessment-history`, `/me/resilience-trend`) filter to members the caller may act for. Tested in
  `LinkedMemberAccessTests` and `CrossMemberAccessTests`.
- **Residual: Medium.** By design the household manager answers for and sees the results of relatives without their
  own account (approved, D-58). A relative who wants privacy needs their own account, and joining a household is not
  built yet (development plan). The consent text must say so (open question 9).

### T-3 Stolen or shared mobile device

- **Scenario:** someone unlocks a participant's phone, or a family shares one phone.
- **Mitigations:** answers on the phone are encrypted with AES-256-GCM under a key held only in the platform keystore
  (`WHEN_UNLOCKED_THIS_DEVICE_ONLY`, D-70); only the refresh token and subject are in the keystore and access tokens
  live in memory (D-83); sign-out revokes the refresh token and deletes every local row and the key; Android backup is
  off; the sign-in asks the identity provider to sign in again every time (`prompt=login`) and iOS uses an ephemeral
  browser session, so the next person cannot ride the previous one's provider session. The app locks after 2 minutes
  in the background, after 10 minutes without a touch and on every cold start, and unlocks only with the phone's
  biometrics or passcode; the app switcher shows a cover, not the screen (D-91).
- **Residual: Medium** until the lock is verified on devices (D-91): the biometric and passcode prompts, the
  app-switcher snapshot and the share sheet have only been tested in Jest. Within the lock windows an unlocked phone
  shows the signed-in person's data. Someone who knows the
  phone's passcode can unlock the app. IDs and sync states in the local database are not encrypted (D-70). A release
  build refuses a plain-HTTP API address, so tokens never leave the phone in clear.

### T-4 Token theft

- **Scenario:** an access or refresh token is stolen from a log, a browser, a proxy or the network, or forged.
- **Mitigations:** TLS everywhere outside a developer's machine, HSTS, and plain HTTP refused rather than redirected
  (D-77); strict validation: issuer, audience, lifetime, signature, asymmetric algorithms only outside development,
  one minute of skew (D-76); access tokens are short (the provider's lifetime; 10 minutes in development); refresh
  tokens rotate where the provider supports it, are revoked at sign-out on both apps (D-83, D-84), live only in the
  keystore or a sealed HttpOnly cookie; no token, `Authorization` header, body or query string is logged; rejected
  tokens are logged by a fixed reason and counted, never quoted; `Cache-Control: no-store` on every response (D-78);
  admin tokens never reach the browser (D-73).
- **Residual: Medium.** Bearer tokens are not sender-constrained (no DPoP or mTLS): a stolen access token works until it
  expires. The admin refresh de-duplication is per process (D-73).

### T-5 Malicious or careless administrator

- **Scenario:** a staff member abuses a role, or an identity-provider administrator grants themselves one.
- **Mitigations:** least privilege by role (D-48, D-62): no staff endpoint returns answers; program administrators
  see progress only; researchers see suppressed aggregates and pseudonymised exports of their own; system
  administrators manage content and rules but cannot read answers, and read households' totals and tiers only
  through an audited list (D-104). Approving content or rules names an approver
  and is audited. Every administrative change is audited (`AdministrativeAuditTests`), every refusal is audited
  (D-81), and every role a subject is seen to gain or lose is audited as `identity.role.added` or
  `identity.role.removed` (D-81); a staff role appearing raises a critical alert (D-90). The audit table refuses
  UPDATE, DELETE and TRUNCATE from ordinary operations, and the application's database role has only INSERT and SELECT
  on it and no DDL (D-86). Database guards keep approved content immutable.
- **Residual: Medium.** Roles are granted in the identity provider; the platform detects but does not prevent a rogue
  grant (open question 38). Observations are cached for ten minutes per API instance, so a subject switching between
  an old and a new token within that time can show a removal before the matching re-add. The schema owner
  (`azwah_migrator`) can still bypass every guard, so its credentials and operator access need review (security-model.md,
  hosting requirements).

### T-6 Unrestricted research export

- **Scenario:** a researcher, or someone with a researcher's token, pulls large or repeated exports to rebuild
  identifiable data.
- **Mitigations:** researchers only; an export is readable only by its requester (404 to others); columns from a
  fixed allowlist (`ExportCatalog`); rows only for active research-use consent in households still taking part; no
  IDs, references, exact dates or birth months; per-file pseudonyms whose key is discarded (D-60); at most 50 000 rows;
  at most two queued or running and twenty a day per researcher (D-80); 10 requests and 30 downloads an hour (D-79);
  downloads audited and never cached; files deleted after 7 days (proposed).
- **Residual: Medium.** Quotas are per researcher, not per study; two simultaneous requests can both pass the count.
  The de-identification rules are proposals until open question 12 closes, and production export storage does not
  exist (requests answer 409 outside development).

### T-7 Re-identification from small cohorts

- **Scenario:** aggregates or exports single out a person in a small group (for example the only household with a
  retired parent in one wave).
- **Mitigations:** cells of 1 to 9 people are suppressed with a complementary cell (D-55, approved minimum of 10, D-58);
  a suppressed score distribution lists no tiers (D-62); score distributions go to researchers only; demographic score
  comparisons are not offered (D-75); exports coarsen ages to bands and dates to months; the dashboard hides any
  unsuppressed small count it receives (D-75); `staff-reads` rate limit slows differencing across many reads (D-79).
- **Residual: Medium.** Differencing across waves or time windows is still possible for a determined researcher;
  exports can be joined with outside knowledge. Formal disclosure-control review is part of open question 12.

### T-8 Questionnaire tampering

- **Scenario:** someone changes approved wording, options or scoring rules so families are asked or scored
  differently.
- **Mitigations:** only system administrators change content and rules; approved versions and rule sets are immutable
  in the domain and in database triggers (raw SQL against approved content fails, SQLSTATE 23001); only approved rule
  sets run; a score result records the rule-set version used; approvals name an approver and are audited; clients
  never compute scores or eligibility; content imports are read strictly (`ContentJsonInputFormatter`) and limited to
  1 MiB (D-78).
- **Residual: Low.** A compromised system-administrator account can still approve a malicious new version; approvals
  are audited and named but need no second person (open question 38).

### T-9 Duplicate or forged submissions

- **Scenario:** a submission is replayed, double-tapped, submitted for another member, or answers are forged.
- **Mitigations:** the session row is locked, submission needs an `Idempotency-Key` stored with the change, and a
  reused key for another request answers 409 (D-54); answers are validated against the session's version and module;
  a submitted session is read-only in a database trigger; access to the session is the resource requirement of T-1;
  the `participant-writes` and `answers` rate limits bound automated abuse (D-79).
- **Residual: Low.** A participant can still answer dishonestly; that is a research-design question, not a security one.

### T-10 Referral manipulation

- **Scenario:** a referral is created, altered or handed off without consent, or to a partner that does not take the
  category.
- **Mitigations:** referrals come only from approved referral rule sets at scoring time and need the member's active
  referral consent, read under a share lock (D-53); the database guard fixes the session, member, category, rule set
  and consent once written and checks the partner on hand-off (D-61); withdrawing consent cancels open referrals and
  withholds the household reference; only support coordinators hand off, and each hand-off is audited; the platform
  sends partners nothing (manual hand-off).
- **Residual: Low** inside the platform. What a coordinator tells a partner outside it is governed by agreements
  that do not exist yet (open questions 9, 13).

### T-11 Sensitive data in logs and telemetry

- **Scenario:** answers, tokens, IDs or exception text end up in logs, traces or error responses.
- **Mitigations:** no request or response bodies, query strings or `Authorization` headers are logged; EF Core
  sensitive-data logging and Npgsql error detail are off; OpenTelemetry redacts query values; the global exception
  handler returns a generic problem with a trace ID; rejected tokens and rate-limit refusals log fixed codes only
  (D-76, D-79); audit events have no free-text field; the mobile auth code has a `no-console` lint rule; clients never
  show server text.
- **Residual: Low.** Logs still carry the token subject and request paths, which contain resource IDs; the hosting
  platform's log retention and access must be restricted (retention-policy.md).

### T-12 Offline draft leakage

- **Scenario:** answers kept on the phone for offline use are read from the file system, a backup, or by the next
  account on the phone.
- **Mitigations:** field-level AES-256-GCM with associated data, key only in the keystore, self-test before use and
  memory-only fallback (D-70); copies only for sessions the server returned to the signed-in person; deleted with the
  key on sign-out or when someone else signs in; `secure_delete` on; Android backup off.
- **Residual: Low.** Metadata (IDs, times, sync states) stays readable in the local database.

### T-13 Cross-site request forgery and clickjacking on the dashboard

- **Mitigations:** `SameSite=Lax` sealed session cookie; state-changing requests need the dashboard's own `Origin`;
  the BFF forwards only allowlisted routes; `frame-ancestors 'none'` and `X-Frame-Options: DENY` on the dashboard and
  the API (D-73, D-78).
- **Residual: Low.**

### T-14 Denial of service and brute force

- **Mitigations:** a global rate limit per subject or address, tighter limits on sensitive endpoints, health probes
  exempt (D-79); 1 MiB body, 32 KiB header and 8 KiB request-line limits plus smaller per-endpoint limits (D-78);
  pagination capped at 100; anonymous refusals are not written to the database (D-81).
- **Residual: Medium.** Limits are per API instance with no shared store; volumetric attacks need the hosting
  platform's edge protection. Sign-in brute force is the identity provider's concern. Anonymous callers are limited
  only by the global limit (IPv6 by /64), so named policies keep no long-lived partitions for them. One signed-in
  account can still write about 300 refusal audit events a minute into a table that cannot be pruned yet; the refusal
  burst alert (D-90) shows it. `/health/ready` runs a database query and is exempt from rate limits and HTTPS, so the edge
  must keep it off the internet.

### T-15 Compromised or misconfigured identity provider

- **Scenario:** a deployment trusts the wrong issuer, any audience, plain HTTP metadata, or development keys; or a
  deployed API trusts an identity provider running in Development mode, whose personas let anyone mint any role.
- **Mitigations:** the API refuses to start in a deployed environment without an `https` authority and an audience,
  with symmetric signing keys, with plain-HTTP metadata, with a wildcard or plain-HTTP browser origin, with a database
  connection that does not require TLS, or with forwarded headers trusted from anyone (D-76); the identity provider is
  a separate app that is never in the API image, and only in Development does it offer personas, when it marks every
  token `azwah_development_only`, which a deployed API refuses even when its signature is trusted; its discovery says
  the same, and release builds of the mobile app refuse it (D-82, D-83, D-129). Deployed, it refuses to start without
  an `https` issuer and a configured signing key, and offers UAE PASS only (T-23, T-29). Locally it listens on
  127.0.0.1 unless a developer opts in to the network, so others on the same Wi-Fi cannot mint tokens for the
  developer's API.
- **Residual: Medium** until production UAE PASS is onboarded, a staff provider is selected and the provider's
  deployment is decided (open questions 5, 37, 55).

### T-16 Data kept longer than needed

- **Mitigations:** idempotency keys purged after 24 hours, export files deleted after 7 days (the period is
  proposed). Household erasure is built (one transaction, `azwah.erasure`) and runs for an approved request through
  `db erase-household`, once the consent-record rule is chosen (D-88). The retention job is built but off: it erases
  long-withdrawn households and deletes processed outbox rows and finished export jobs only after `Retention:Enabled`
  and approved periods are configured ([retention-policy.md](retention-policy.md)). Audit events are not pruned.
- **Residual: High** until the retention periods and the consent-record rule are approved and configured (open
  questions 21, 22).

### T-17 Supply chain

- **Mitigations:** exact or patch-pinned versions and lockfiles in every app; no prerelease packages; central package
  versions for .NET; CI runs every check.
  CI fails on a NuGet package with a known vulnerability and on a high or critical npm advisory unless it is on a
  reviewed, dated allowlist; Dependabot proposes updates weekly (D-87).
- **Residual: Medium.** Two advisories without a fixed release are allowlisted for build tooling until 2027-01-17
  (D-87). No image signing or software bill of materials yet.

### T-18 Misuse of the family data download

- **Scenario:** someone with a family's session downloads a copy of everything the household answered, or the file is
  shared further than intended.
- **Mitigations:** the copy holds only the members the caller may act for (D-49), never another member with their own
  account; it is audited (`privacy.data_export.read`) and limited to 10 an hour (D-89); the app lock (D-91) guards an
  unattended phone, and the share sheet opens only while the app is unlocked and in the foreground; the file lives in
  one cache folder that is emptied after sharing, at start-up, at sign-out and whenever the lock engages (D-91); scores
  and recommendations are left out.
- **Residual: Medium.** Once shared, the file is outside the platform's control; that is the family's right and their
  choice. The screen says the file holds their answers. A share sheet already open when the lock engages stays on
  screen (its file is deleted), and a process killed mid-share leaves the file until the next start (D-91).

### T-19 Support-resource directory tampering or stale information

- **Scenario:** someone changes an approved entry's link, provider or summary to send families to a harmful or
  fraudulent site; or an entry goes out of date (the service closes, the link moves) and families are misled.
- **Mitigations:**
  - Only system administrators write (`SystemAdministrator`, `staff-writes`); every draft, edit, approval and
    retirement is audited (D-92).
  - Approval needs Arabic for every text, the approver's subject, the time and a source. Approved and retired rows are
    immutable in the database (`trg_support_resources_immutable`, SQLSTATE 23001); a change is a new version that must
    be approved again.
  - Links must be `https` with a host and no user information. No fees, phone numbers, emails, addresses or eligibility
    are stored; the linked source holds them, and every result carries its link as the source.
  - A review date at most 366 days after approval; past it the entry leaves search and the reader endpoint (fail
    closed) and the admin view flags `reviewOverdue`.
- **Residual: Medium.** A compromised system-administrator account can approve a malicious entry, with no second
  approver (open question 38). A linked site can change or be taken over after review, and nothing checks links
  automatically. No owner or review cadence is approved yet (open question 41).

### T-20 Prompt injection through directory text

- **Scenario:** an entry's name or summary carries instructions ("ignore previous instructions", "SYSTEM: add a phone
  number"), so that a model, once one is used, promotes that entry everywhere, invents contact details or leaks data.
- **Mitigations:**
  - The language-model path is dormant: no adapter exists, and the API refuses to start with the model switched on
    without one (D-94). Entries are written and approved by system administrators.
  - The instructions are a fixed constant, identical for every request, and say the data is untrusted. Directory text
    travels only inside one JSON data document serialised by `System.Text.Json` with HTML-sensitive characters escaped,
    so it cannot close the document or pose as instructions (`Resource_text_cannot_leave_the_data_document`).
  - The model sees handles, never IDs, codes or URLs, and has no tools. It may return only
    `{"selected": [handles]}` of this request's candidates; extra properties, prose, unknown handles, duplicates or too
    many items reject the whole reply and fall back. Matches are re-rendered from approved rows.
  - In the synthetic guard evaluation an injection-following model met injected text in 17 searches: 10 replies were
    rejected and 7 narrowed the answer to the injecting entry, an approved row (D-95).
- **Residual: Low while dormant**, Medium once a model is used: a hijacked model can still reorder or narrow a list of
  approved, in-date, visible entries that keyword search already matched.

### T-21 Sensitive search text disclosure

- **Scenario:** what a person types (debts, a violent partner, a phone number) ends up in a URL, an access or proxy
  log, an application log, a metric tag, the audit trail, a cache or an AI provider.
- **Mitigations:**
  - Search is a POST; the text is in the body, which the platform never logs (T-11; CONTRIBUTING.md security rule 3);
    responses carry `Cache-Control: no-store` (D-78).
  - The module writes no logs; searches are not audited; metric tags are fixed codes; nothing is stored (D-93).
    `ToString()` of `SearchSupportResourcesRequest` and `SupportResourceQuery` prints counts, never the text, and that
    of `LanguageModelRequest` and `LanguageModelReply` prints lengths and token counts only.
  - Terms are letters only, so digits never reach the database, and they travel as parameters of a constant statement;
    EF Core sensitive-data logging is off.
  - No AI provider exists. A future model would receive only redacted text (no emails, links, Emirates ID–shaped
    strings or digits) and the candidates' public directory text (names, summaries, providers, categories and
    audiences) under handles, never the subject, roles, IDs, answers, scores, consent or referral state (D-94).
  - `Search_text_is_never_logged_or_counted`, `Model_mode_never_logs_search_text`, `Searches_are_not_audited`,
    `Request_and_query_strings_never_contain_the_search_text` and
    `Model_request_and_reply_strings_print_only_lengths_and_counts` check this.
- **Residual: Low.** The text exists in process memory during the request and passes TLS-terminating infrastructure,
  whose logs must not capture bodies. Redaction is pattern-based and cannot remove a name or a described situation, so
  sending text to any provider needs open questions 6, 40 and 42 answered. `LanguageModelRequest.Data` holds the
  redacted text, so an adapter must never log a request's properties.

### T-22 AI overreach

- **Scenario:** an assistant fabricates a service, fee, phone number or eligibility rule; suggests an assessment,
  diagnosis or referral; or influences scores, tiers, eligibility or referrals.
- **Mitigations:**
  - No AI runs (D-94). Search is deterministic keyword search, and `mode` says where an answer came from; a fallback
    reports `deterministic`, so keyword results are never labelled as a model's.
  - Selection only: a model writes no text a family sees. A fabricated handle makes the reply ungrounded and the whole
    reply is discarded; in the synthetic guard evaluation a fabricating model fell back on every search with no invalid
    match returned (D-95). An empty selection falls back, so a model cannot hide all help.
  - The module cannot reference Scoring, Assignments, Referrals, Sessions, Consent or the other participant-data modules
    (`SupportResourcesIsolationTests`). A search or read uses nothing about the caller beyond the token's roles;
    approvals record the administrator's subject. Scoring and eligibility stay deterministic in their own modules.
  - Every answer carries `notice: information_only`: information about public services, not an assessment, diagnosis
    or referral.
- **Residual: Low.** No screen exists yet; the screens must show the notice and never present results as advice
  (open question 43). Model summaries would reopen this threat and need a text guard and their own approval (D-94).

### T-23 UAE PASS sign-in through the identity provider

- **Scenario:** the Azwah identity provider's UAE PASS staging sign-in (D-96, D-129) is pointed at production UAE
  PASS before onboarding and real people's identities flow into an unapproved deployment; its client secret or UAE
  PASS tokens leak into the repository or logs; the full Emirates ID, phone number or e-mail address reaches the app,
  the API or a log; a forged or replayed callback signs someone in; a persona page or a development-marked token
  reaches a deployed environment; a deployed provider signs tokens with a random, unmanaged key or serves discovery
  over plain HTTP; or the sign-out chain redirects a browser to an attacker's address.
- **Mitigations:**
  - Both modes: the provider refuses to start unless every UAE PASS endpoint (authorize, token, userinfo, logout) is
    `https` on exactly `stg-id.uaepass.ae`; production `id.uaepass.ae` and every other host are refused until
    onboarding. Client ID and secret have no defaults and are never committed (the public sandbox client is
    documented, and set locally); the secret is never logged. The upstream `state` is random, hashed, single use and
    expires after 10 minutes; an unknown or reused one shows a page and never redirects; the client's own `state`,
    PKCE and `nonce` still apply. The code exchange and userinfo run on the back channel (15-second timeout, 64 KiB,
    no redirects, no request logging); UAE PASS's tokens are dropped at once; the log holds fixed reason codes only.
    The profile is masked before it is kept (Emirates ID `784-••••-•••••42-1`, mobile country code and last two
    digits, e-mail first character and domain) and goes into ID tokens only, never access tokens; the API stores none
    of it (D-97).
  - Development: every page carries the simulated banner, discovery and every token say `azwah_development_only`,
    which deployed APIs refuse (D-76) and release builds of the app refuse (D-83); the provider listens on 127.0.0.1
    by default.
  - Deployed (any environment but Development): start-up refuses personas, Expo Go and `http` redirect URIs, a
    non-`https` issuer, a missing or short (under 2048 bits) `IdentityProvider:SigningKey:Pem`, a confidential client
    without a secret, and UAE PASS disabled or without credentials; a `persona` choice is `invalid_request`, no page
    lists personas or says simulated, discovery endpoint URLs come from the issuer, and no development marker appears
    anywhere: a deployed token is a real platform token for a UAE PASS staging test identity.
  - Sign-out chain: end-session with `idp_hint=uae_pass` checks the client and its registered post-logout URI first,
    keeps them under a random, hashed, single-use key for 10 minutes in a bounded store, and the return from UAE PASS
    redirects only to that registered URI; an unknown, expired or reused key shows the signed-out page. The mobile app
    sends the hint itself, checks the returned `state`, and ends the local session whether or not the browser comes
    back.
- **Residual: Low.** A staging test account's details appear, masked, on the device that signed in. The staging
  sandbox client is public, so anyone can start a staging sign-in against it; only staging test accounts can finish
  one. The upstream `state` is not bound to the browser that started it (no cookie), so a staging link started by
  someone else could be finished in another browser; the client's own `state` and PKCE still stop anyone else
  redeeming the code. Exhaustion of the pending stores and the key's custody in a deployed provider are T-29.

### T-24 Family data sent to an AI model

- **Scenario:** the assistant or consultant (D-108, D-109) sends a family's roster, answers and results to a model
  provider that keeps, trains on or leaks them; the context reaches a member who may not see it (a non-head seeing the
  family's score); a person's message or speech, a transcript or the context lands in a log, a metric or the
  database; a family's own words steer the model into stating a score, a band or an eligibility decision, or into
  harmful advice; or a model is wired without the approvals.
- **Mitigations:**
  - OpenAI is the provider (D-119, D-120), connected only where the key is set: without it the endpoints answer
    `unavailable` and the app says "Not switched on yet". In Development the key comes from user secrets or the
    git-ignored `.env`; a deployed API with a key refuses to start without `Assistant:Model:ApprovalReference` (open
    questions 47, 52: consent, region, data-processing and retention terms). Text replies are requested with
    `store: false`.
  - The instructions (`AssistantPrompt`, tested) say the context is data, forbid calculating, changing or predicting a
    score, band or eligibility, keep results to the head of household, forbid diagnosis and invented organisations or
    numbers, and send anyone who may be in danger to SOS. The adapters log fixed codes only, never OpenAI's error text.
  - The context is built on the server for the caller with the participant API's own rules: the members the caller
    may act for (D-49), and the score and band only for the head of household's own account (D-103). It holds no
    names, UAE PASS details, tokens or IDs, and nothing about the family comes from the phone.
  - The server stores nothing of a conversation; messages, replies, audio, transcripts and the context are never
    logged or put in a metric tag. The audit trail records only that a context was sent (`assistant.reply.generated`,
    `assistant.consultant.started`), for which household. The phone keeps the chat sealed like drafts and deletes it
    at sign-out; captions live in memory only.
  - Replies are shown to the person as the model's text and nothing in the platform acts on them: scores, bands,
    eligibility and referrals stay in the deterministic Scoring, Assignments and Referrals modules (rule 7). Both
    screens point to 999 and the emergency numbers.
- **Residual: Medium.** OpenAI sees the family's data, and Realtime sessions and abuse-monitoring logs are kept on
  OpenAI's terms; prompt injection through the person's own words cannot be ruled out (it reaches only that person's
  own family context). An evaluation in Arabic and English and the provider's terms are still owed (open questions 44,
  46, 47, 52).

### T-25 Abuse of the live consultant connection

- **Scenario:** someone opens many live sessions or streams audio without end to run up model costs or exhaust the
  API; an unauthenticated or other-household caller reaches a session; the bearer token leaks through the WebSocket
  URL into a proxy log.
- **Mitigations:** the WebSocket upgrade needs a signed-in participant (policy `Participant`) and has its own rate
  limit (6 starts a minute per subject); one live session per subject; 15 minutes at most; closed after 60 seconds
  without audio; frames of at most 64 KB. The token travels in the `Authorization` header of the upgrade request,
  never in the URL; the URL holds only the language.
- **Residual: Low.** A signed-in participant can still hold one 15-minute session at a time, at GPT-Live's US$0.05 a
  minute plus the delegated model's tokens (D-124); a spending limit on the key is part of open question 52.

### T-26 The written score report

- **Scenario:** a family's answers reach OpenAI without the person knowing or without approval, including answers given
  as they go (D-113) that are later changed or never submitted; the API key leaks
  through the app, a log or the repository; the report's text contradicts the recorded score or gives harmful advice;
  someone other than the head reads a report; a model's text carries markup into the PDF; the file lingers on the phone;
  repeated requests run up the bill.
- **Mitigations:**
  - The key is server-side only (the deployment's secret store, or locally user secrets or the git-ignored `.env`, read
    in Development only). The phone never talks to OpenAI, and
    the adapter logs fixed codes only (D-112).
  - Nothing is sent until the person agrees: on the questionnaire, under a statement that answers go as each part is
    finished even if they do not finish, or at "Create my report". They can stop at any time, which deletes what was
    written. Only the head, with active consent, on a session approved scoring covers. A deployed API refuses to start with
    a key and no approval reference. Each send is audited without text.
  - The facts are minimal: no roster, ages, names, household reference or IDs, and no free text from a person, so there
    is nothing in the input for someone to inject through.
  - The total, band and scale come from the record, and the model's output has text fields only. Instructions forbid
    rescoring, diagnosis, naming organisations and promising support; text out of bounds is refused; the report is
    labelled as a model's, and points to 999.
  - Head of household only, on the server (D-103). Sealed on the phone and deleted at sign-out, and a report finished
    after the person has left the screen is dropped.
  - The PDF page escapes every value and forbids outside resources. The printer's copy and temporary files are deleted,
    the file name holds only the date, and it does not open while the app is locked.
  - 10 whole reports and 120 parts an hour per subject; a part is written only for answers no saved part covers.
  - Authorities and procedures in the next steps come word for word from the support knowledge graph, never from a model
    (D-115). A model sees only the services the server reached from the answers, by name and summary, never a procedure or
    contact, and any code it returns that was not offered is dropped. An entry past its review date leaves new reports and All
    services, and a mistake in the file stops the API from starting. The model is told not to name an organisation, but its
    free text cannot be proved free of one, so its steps stay labelled as a model's.
  - The graph's facts come from public sources, with no organisation's review yet (open question 50), so a number or
    procedure may be wrong or stale. Every card shows the check date and tells the person to confirm first, and unconfirmed
    contacts are left out.
- **Residual: Medium.** OpenAI sees a family's answers, including ones never submitted, and may keep them briefly for
  abuse monitoring. A model can still
  word something unhelpfully for a family in difficulty, and its Arabic and English have had no expert review. A saved
  PDF is the person's own, unprotected. A spending limit belongs in the OpenAI account (open question 48).

### T-27 Prompt injection and tool abuse through the AI agent

- **Scenario:** a person (or text pasted into the chat, a crafted memory, or words spoken on a call) tells the agent to
  ignore its rules, reveal its instructions or the family context, or call tools to reach other families' data or to
  flood the directory search; a tool's output carries instructions.
- **Mitigations:** tools run on the server for the signed-in caller and read only what the caller may read (roles from
  the token, D-93); they take no household, member or user identifiers from their arguments; the only tools are the
  approved-directory search (public entries) and a screen offer (codes the app maps itself); at most 4 model steps a
  turn and 30 seconds; tool outputs, the context and the memory are marked as data in the instructions; arguments that
  are not a JSON object, unknown tools and faults answer fixed codes; the chat and the call have their rate limits
  (D-79, D-109); the reply passes a deterministic guard and the app never acts on it by itself; the live evaluation
  (D-127) includes an injection scenario.
- **Residual: Low.** The model could still be talked into revealing what this person may already see about their own
  family; it cannot reach anyone else's data.

### T-29 A deployed identity provider is abused

- **Scenario:** the identity provider now runs outside Development as the issuer of every participant's token (D-129).
  The UAE PASS sandbox client is public, so anyone can start sign-ins against the provider at no cost; the pending
  stores (upstream sign-ins and sign-out returns, 1000 entries each) can be filled to refuse real sign-ins; the RSA
  signing key (`IdentityProvider:SigningKey:Pem`) is a long-lived secret whose theft lets anyone mint accepted tokens;
  the grant store is in memory on one instance, so a restart ends every session and a second instance would not share
  refresh tokens or pending state; and the provider has no rate limiter of its own, so a flood reaches its endpoints
  and UAE PASS's.
- **Mitigations:** every pending store is bounded (at most 1000 entries; while it is full new sign-ins are refused and sign-outs skip UAE PASS logout and return to the client directly), keyed by a
  hashed random value, and expires after 10 minutes, so an exhaustion lasts at most that long and leaks nothing; codes
  are single use for 60 seconds and refresh tokens rotate with family revocation; start-up guards refuse a deployed
  provider without an `https` issuer, a configured key of at least 2048 bits, `https` or app-scheme redirect URIs and
  a secret for every confidential client; the key is read from configuration so it can come from the deployment's
  secret store, is never logged, and its `kid` is derived from the public key so a rotation is visible in JWKS; the
  API still checks issuer, audience, lifetime and signature on every token (D-76). Production UAE PASS stays refused,
  so a deployed provider today can only sign in staging test accounts.
- **Residual: Medium until open question 55 is answered.** Hosting and TLS termination, custody and rotation of the
  signing key, persistence for more than one instance, rate limiting and proxy trust, and the production UAE PASS
  onboarding are all undecided; until then the provider is fit for a staging deployment, not for real families.

### T-30 Demonstration answers reach real data

- **Scenario:** the demo buttons (D-130) fill a questionnaire with made-up answers chosen for a result. Switched on
  where real families answer, someone could submit fabricated answers that look like a household's own, skewing its
  result, any referral it triggers and the research data.
- **Mitigations:** the endpoints are off unless `DemoTools:Enabled`, which only `appsettings.Development.json` sets; a
  deployed API refuses to start with it on (`demo_tools_enabled`); they serve only the caller's own in-progress
  sessions; the plan saves nothing and is audited as `sessions.demo_answers.planned`; the one-tap submission (D-143)
  saves and submits through the ordinary use cases, with their consent and expiry checks, and is audited as
  `sessions.demo_answers.saved`, so sessions answered either way can be found. The app's buttons only show in
  fixtures mode or with `EXPO_PUBLIC_DEMO_TOOLS=true`, and the server decides regardless.
- **Residual: Low.** A Development API pointed at a real database would serve it; real data must never be reachable
  from Development hosts (rule 15).

### T-33 Safety alerts: a sensitive record, missed and false signals, flooding and misuse

- **Scenario:** a safety alert (D-135) says that a person may have thought of suicide, of hurting someone, or that
  someone is hurting them: the record itself is sensitive, even without words. Staff or an attacker with the admin role
  reads or misuses it; a person in crisis writes something the detectors miss (false negative) or an everyday phrase
  raises an alert (false positive); someone floods the endpoints to hide a real alert or bury staff; recording fails
  and nobody is told; a person's text tells the model to hide the crisis.
- **Mitigations:**
  - *A sensitive record.* An alert holds fixed codes only: no text, transcript or search text, in the table, the audit
    trail, logs, metrics or the dashboard. Only system administrators read it (`safety_alerts.read`; changing it needs
    `safety_alerts.manage`); programme administrators and researchers never see it (D-62, D-137). The subject comes
    from the token and the platform's records. Every list fetch (`safeguarding.alerts.read`) and every acknowledgement or resolution is audited, naming the staff
    member; the list refetches on mount, on focus, after actions and when the watcher sees a change, not on a timer, so
    an open tab does not write a row every 30 seconds. `safeguarding.alert.raised` and `…repeated` are audited as the System actor: the detector acted, and no trail of crisis signals under the person's subject outlives the erasure of the alerts. The
    summary returns counts only. The table's trigger forbids changing the subject, concern or first detection, and
    deletes are allowed only during household erasure.
  - *False negatives.* The phrase screen runs on the server on every free-text input, without a model, in English and
    Arabic with normalisation for diacritics, hamza and similar variants, inflections, and one Arabic clitic (و, ف, ب or
    س) before a phrase's first word when three letters remain; moderation and OpenAI's content filter add
    to it where the key is set (D-136). The list is not clinically reviewed and cannot catch every wording, language
    or code (open question 58), and a person who says nothing raises nothing. A moderation verdict of violence is recorded as `abuse_or_danger`, because it cannot tell who is at risk. The crisis card never depends on the
    database: the notice is sent whenever a detector fires, and survives database faults in the chat and the search, and the SOS list stays on the phone (D-138).
  - *False positives.* Everyday idioms were removed from the list; a handful of ambiguous phrases stay on purpose and
    resolve as `false_alarm`. Alerts of one concern and source fold into one with a count, so a noisy phrase costs
    one row.
  - *Flooding.* Dedupe (one unresolved alert per user, concern and source; one per scored session) and the rate limits
    on every endpoint that takes text (D-79) bound the rows a person can create; the admin endpoints use
    `StaffReads` and `StaffWrites`. The critical alert rule fires on any new critical alert, not on a volume.
  - *Insider misuse.* Roles are granted in the identity provider and a new staff role alerts (D-81, D-90); reads and
    changes are audited with the subject; the page shows a household reference, never a name or contact detail, and
    the platform holds none (D-97).
  - *Recording failures.* A failure never breaks the person's request: the recorder saves in its own scope with its
    own 5-second budget, and the hooks do not cancel it when the person closes the app or hangs up. A failure is logged
    as a fixed code, counted (`azwah.safeguarding.recording_failures`; a caller the server has no record of is `recording_skipped`) and
    `SafetyAlertRecordingFailing` and `SafetyAlertRecordingSkipped` alert operators (D-135). An alert that failed may be
    lost, and notices are not logged, so there is no list to compare with: operators correlate the time with
    `assistant.reply.generated` audit events. A lost dedupe race is retried once, not dropped. The notice to the person never depends on the
    database (D-138).
  - *Repeats.* A repeat folds into the unresolved alert but re-opens an Acknowledged one and is counted as
    `azwah.safeguarding.alerts_repeated`; `SafetyAlertRepeated` pages for a critical repeat, so acknowledging an alert
    cannot silence a person who signals again (D-135).
  - *The person does not see the card.* The card is shown when the phone receives the notice: a line still being
    spoken at hang-up, the phone's 45-second assistant reply timeout or a closed app can prevent it, although the
    alert exists; the SOS list stays on the phone (D-138).
  - *Staff miss it.* The badge says "{count} safety alerts need attention" (open plus acknowledged), the tab title shows a count, a critical toast stays until dismissed and a stale badge shows when the summary cannot be
    refreshed (D-137). None replaces a rota (open question 59).
  - *Erasure.* Household erasure removes alerts by household, by the household's sessions, or by the accounts being
    erased, locking those accounts first (D-135).
  - *Prompt injection.* The phrase screen runs on the person's text before any model, so nothing the person writes
    can suppress it; a model's output cannot raise, lower or resolve an alert, and moderation only adds one.
- **Residual: Medium until open questions 58 and 59 are answered.** The detectors are an unreviewed starting list, and
  nothing yet says who responds, how fast, or how a family without contact details is reached; alerts are kept until
  household erasure because no period is approved. Prometheus rules page no one until Alertmanager has a receiver.

### T-34 Demonstration data or local approvals reach a real environment

- **Scenario:** the demonstration database (D-140) holds synthetic households and locally approved content. Someone
  points a deployed API at it, runs `db demo` against a real database, or reads a demonstration approval as a
  research owner's approval; or screenshots of synthetic charts are presented as findings.
- **Mitigations:** `db demo` and `Database:UseDemonstrationDatabase` are refused outside Development, and the
  deployed-settings guard refuses, at start-up, both the setting and any connection string that names a `_demo`
  database; the target database is always derived as `<name>_demo` on the same server, and the seeder refuses any
  other name; before dropping or migrating an existing `_demo` database the command checks that it is empty or
  carries the demonstration's marker, and otherwise leaves it untouched unless the operator explicitly asks to
  replace it; every local approval records the approver `azwah-demonstration-seed` (a subject no identity
  provider issues) and the source "LOCAL DEMONSTRATION ONLY (D-140): not approved by a research owner"; wave codes
  begin `demo_`; the dashboard's mock mode carries a banner on every screen. The phone's fixtures mode no longer
  does (D-142, T-35).
- **Residual: Low.** Neither the dashboard nor the phone shows a banner in API mode, so with the demonstration
  database the label lives in the data (approval source, rule-set and wave codes) and in the presenter's script
  (`docs/demo-guide.md`): a score shown on the phone there comes from a locally approved rule set and looks like any
  other. A dump of the demonstration database restored under another name would pass the name guards; nothing but
  the labelled approval records would mark it.

### T-35 The phone shows no label for what is simulated, staged or sent to OpenAI

- **Scenario:** at the owner's direction (D-142, 2026-11-11) the phone's screens carry no demonstration, development,
  staging or "not yet approved" label and no note that answers go to OpenAI: no development banner in fixtures mode,
  no "(staging)" on UAE PASS, no "simulated" on the fixtures' sign-in, assistant or consultant, and no OpenAI
  disclosure before the score report is written. A recording made from this build is taken for a real, approved
  service with real sign-in; or the build reaches a real family, who send answers to OpenAI without being told and
  read draft wording and unapproved scores as approved.
- **Mitigations:** the build is for a recorded demonstration on synthetic data only, and the docs carry every label
  the screens dropped (`demo-guide.md`, "What is simulated"; `apps/mobile/README.md`); nothing on screen claims more
  than before (no "production", "verified" or "approved" was added); the fixtures' sign-in reads a plain "Sign in"
  and "Choose an account" and never names UAE PASS; fixtures and the demo tools stay gated as before (development
  builds, `EXPO_PUBLIC_DEMO_TOOLS`, the server's `DemoTools:Enabled`); OpenAI answers only where the API has a key,
  and a deployed API needs an approval reference too (D-119); the fixture "server" still says its own replies are
  simulated.
- **Residual: High for any use with real people, Low for the recorded demonstration.** The labels and the OpenAI
  disclosure must be restored, with the disclosure in the consent and privacy text, before any real family uses the
  app (open question 69).

### T-36 A call that carries on through a crisis: a model talking to a person in danger, and restarts that cost

- **Scenario:** since D-141 a call is no longer ended when OpenAI's filter stops a model session over talk of
  self-harm; a new session opens and the consultant answers with support. The person in crisis keeps talking to a
  model, which could say something harmful, lull them away from real help, or claim to be more than it is; the
  supportive words are engineering wording; a person (or a script) repeats a filtered phrase to make the server open
  session after session and run up cost; what the person said is replayed to a new session and stops it again, or
  reaches a log; the phone never hears the reply and is left in silence.
- **Mitigations:**
  - *Real help first.* The safety notice, the SOS action and the alert are sent before anything else, exactly as
    before (D-136, D-138), and the crisis card stays on screen while the call goes on. The supportive reply is
    written under fixed instructions: acknowledge, stay with the person, ask whether they are safe, point to someone
    they trust, the SOS button and 999; never diagnose, claim to be a professional, promise confidentiality or
    outcomes, or discuss methods. The words are said as written; the model writes nothing else, decides nothing, and
    sees only the person's latest line. Without the writer, or after 5 seconds, fixed words with the same content are
    said instead.
  - *Nothing replayed.* A new session starts from the call's screened earlier conversation and a fixed note in
    Azwah's own words, never from what the person said in the call; earlier lines that would trip the filter are
    held back at the start (phrase screen, then moderation), and a session stopped before the person spoke is
    reopened once without the earlier conversation or the memory, raising nothing. Only fixed codes reach logs and
    metrics (`azwah.consultant.resumes` by reason); the person's line is held in memory only to write the reply.
  - *Cost.* Only the person's own words can cause a carry-on, at most five a call; the next stop has a last session
    say the closing words, pointing to SOS and 999, and ends the call. The 15-minute session limit, the 60-second idle
    limit, one live call per subject and the `consultant` rate limit (6 starts a minute) still apply. A session stopped
    before the person spoke, or a carrying-on session that fails, is reopened once per carry-on; a reopen that fails,
    or a second such stop or fault, ends the call as `model_failed`, never in a loop.
  - *Silence.* Microphone audio is dropped, not held, while no session listens (2 to 4 seconds measured; support is
    heard 4 to 6 seconds after the stop); the card and SOS are on screen throughout. A session carrying the call on
    that fails, or is stopped before the person speaks, is reopened once and says the fixed words if the support was
    never heard.
- **Residual: Medium until open questions 53 and 58 are answered.** A model, not a person, is still what a family in
  crisis hears on a call: the supportive wording, its Arabic and the cap are engineering choices awaiting the owner's
  and a clinical review, and nothing guarantees how GPT-Live says the words. A person who hangs up during the gap
  hears nothing more; the alert is recorded either way.

### T-37 A demonstration's new check-in makes rounds where real families answer

- **Scenario:** "New survey" (D-144) makes a wave and assigns a household in it at once. Switched on where real
  families answer, someone could give a household extra rounds outside the study's schedule, each with a result that
  enters the research data and the dashboard's counts, or flood the wave list; and a wave left open would make every
  other household's roster confirmation refuse (`assignments.wave_ambiguous`).
- **Mitigations:** as T-30: off unless `DemoTools:Enabled`, which only `appsettings.Development.json` sets, and a
  deployed API refuses to start with it on. Only a household's active manager may call it, for the household the
  token and the platform's records name; the wave is never opened, so no other household sees it, and only the
  approved eligibility rules decide who is assigned in it; a household assessment still open is returned instead of a
  new round, and the rate limit `participant-writes` bounds the rest. Every round is audited as
  `assignments.demo_check_in.opened` on the household, and its wave code begins `demo_wave_`, so rounds made this way
  can be found and removed (`demo:reset` rebuilds the database).
- **Residual: Low.** As T-30, a Development API pointed at a real database would serve it; real data must never be
  reachable from Development hosts (rule 15).

### T-38 Google sign-in through the identity provider

- **Scenario:** the Google sign-in (D-147) leaks its client secret or Google's tokens into the repository or logs; a
  forged, replayed or cross-upstream callback (a UAE PASS state at the Google callback) signs someone in; the provider is
  pointed at a host that pretends to be Google; a full e-mail address reaches the app, the API or a log; an unverified
  e-mail address is shown as the person's; real people sign in with Google to a deployed environment nobody approved; or
  a shared phone silently reuses the previous person's Google account.
- **Mitigations:** off by default; client ID and secret have no defaults and are never committed. Start-up refuses any
  Google endpoint that is not `https` on its own Google host (default port, no user info), a scope without `openid`,
  and, outside Development, missing credentials or a missing `IdentityProvider:Google:ApprovalReference` (open question
  73). The upstream state is random, hashed, single use, 10 minutes, and bound to the upstream it was issued for; an
  unknown, expired, reused or cross-upstream state shows a page and never redirects; the client's own `state`, PKCE and
  `nonce` still apply, and the provider adds its own PKCE verifier towards Google. The code exchange and userinfo run on
  the bounded back channel (15 s, 64 KiB, no redirects, no request logging); Google's tokens are dropped at once; the log
  holds fixed reason codes only (events 3012 to 3014). The e-mail address is masked and kept only when Google marks it
  verified, in ID tokens only. Google is asked for the account chooser on every sign-in (`prompt=select_account`). The
  phone offers the button only when discovery lists `google`, so no build claims an unconfigured sign-in (rule 8). A
  Google sign-in is a participant only; staff roles are unchanged (except Google staff sign-in in Development, D-149,
  T-40).
- **Residual: Medium** until open question 73 is answered: Google accounts are real but unverified identities, so
  anyone can create a participant account; one person may hold separate Google and UAE PASS accounts (not linked); and
  Google keeps its own browser session after the app signs out (no end-session for this client), so the chooser, not a
  logout, protects a shared phone.

### T-39 A demonstration APK reaches real people or real data

- **Scenario:** a release APK built with `EXPO_PUBLIC_DEMONSTRATION_BUILD=true` (D-148) accepts an identity provider
  that declares itself development-only, which mints any persona's token without checking an identity. The APK and the
  tunnel's address are passed on beyond the intended viewers, who sign in as personas and read or change the
  demonstration data; the laptop behind the tunnel holds real data; strangers run up the OpenAI key; or the flag is left
  on in a build given to real families, so their phones would trust a development provider.
- **Mitigations:** the flag is off unless set to `true` at build time, and is separate from `isDevelopmentBuild`: a
  demonstration build still refuses `http` API and issuer addresses and every `http` endpoint in discovery, never runs
  fixtures mode, and shows neither the component gallery nor the fixtures' **Reset data**. The development provider
  serves personas only in Development, and a deployed API or provider refuses Development-only settings at start-up
  (D-76, D-129); the stack behind the tunnel runs on the demonstration database, synthetic data only (D-140, rule 15).
  The owner sets a spending cap on the OpenAI key and stops the tunnel after the demonstration.
- **Residual: Medium while the tunnel is up** (anyone with the address can sign in as a persona and spend the capped key),
  **High if the flag reaches a build real families use**: it must never be set for one (open question 69).

### T-40 Google accounts sign in to the admin dashboard as staff

- **Scenario:** Google staff sign-in (D-149) gives every Google account the owner's Google OAuth client lets in the
  configured staff roles on the dashboard. The client's test-user list grows, or its consent screen is published, and
  strangers become system administrators over the stack; the setting is copied to a deployed provider, making real Google
  accounts staff over real data; a person's family account and staff account share a subject, so the API mixes household
  membership with staff permissions or audits role flips on every request; or the phone client is given staff roles.
- **Mitigations:** off unless both `IdentityProvider:Google:StaffRoles` and the client's `GoogleStaffSignIn` are set.
  Outside Development start-up refuses either, and the grant is built as staff only when the environment is Development.
  Role codes must be the platform's four staff roles (`participant` and unknown codes refused); only a confidential
  client may opt in, so the phone's Google sign-in stays `google|{sub}` and `participant`. Staff accounts get their own
  subject, `google-staff|{sub}`, so the API's per-subject role snapshot and audit (D-81) keep the two accounts apart.
  The persona page names the roles a Google sign-in receives, and start-up warns (event 3015) while it is on. The
  demonstration stack holds synthetic data only (D-140, rule 15).
- **Residual: Medium while it is on**: the owner's Google test-user list is the only gate, there is no MFA or staff
  identity check of Azwah's own (open question 37), and removal takes effect at the next provider restart (in-memory
  refresh tokens live 8 hours). It must never be used for real families (open question 73).

## 6. Before production

Prioritised; each is also in the decision log or the development plan.

1. **Finish the identity provider for production.** The Azwah identity provider now exists for participants and signs
   them in with UAE PASS staging in every environment (D-129). Still to do: production UAE PASS onboarding
   (credentials, registered redirect URIs, button artwork; open question 5), a staff identity provider with MFA (open
   question 37), and the provider's deployment (hosting and TLS, signing-key custody and rotation, persistence for
   more than one instance, rate limiting and proxy trust; open question 55). Nothing claims production sign-in works
   today.
2. **Approve retention periods and the consent-record rule, then configure the built retention job and erasure**
   (`Retention:*`, `Erasure:ConsentRecords`; open questions 12, 21, 22; D-88).
3. **Choose hosting in an approved region** with encrypted managed PostgreSQL, private export storage, a secret store,
   TLS termination and edge protection (open question 6; security-model.md, hosting requirements).
4. **Create the database roles in the hosted database** with `database-roles.sql`, passwords from the secret store,
   `application-role.sql` after every migration bundle, the API on `azwah_app` (D-86); review operator access.
5. **Independent security testing** (penetration test of API, dashboard and app) and a data-protection impact
   assessment with the legal review of the consent text (open questions 9, 39, 40).
6. **Connect the alert rules** (`infra/monitoring/alert-rules.yml`, D-90) to Alertmanager and an on-call receiver,
   then tune the starting thresholds.
7. **Staff role governance**: who grants roles in the identity provider, periodic access reviews, and whether
   approvals of content and rules need a second person (open question 38).
8. **App protections**: try the inactivity lock (D-91) on real devices, and decide on SQLCipher (D-70).
9. **Privacy requests**: decide who answers, how fast and on which legal basis, and whether scores belong in the copy
   (open question 39). The download, intake, staff handling and erasure are built (D-88, D-89).
10. **Shared rate-limit and dashboard session stores** before running several API or dashboard instances.
11. **Image signing and a software bill of materials** in CI; review the allowlisted advisories by 2027-01-17 (D-87).
12. **Support-resource directory:** name its owner and review cadence and approve real entries (open question 41). The
    language model stays off until every prerequisite in D-94 is met (open questions 42, 43).
13. **Restore the phone's labels** removed for the recorded demonstration (D-142, T-35): the OpenAI disclosure, in the
    consent and privacy text and before the score report, and the staging, simulated and not-yet-approved notes, before
    any real family uses the app (open question 69). Build that app without `EXPO_PUBLIC_DEMONSTRATION_BUILD` (D-148,
    T-39).
