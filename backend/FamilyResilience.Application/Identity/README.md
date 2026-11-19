# Identity module

**Owns:** Mapping OIDC subjects to platform users (`PlatformUser`, `platform_users`); roles; the current-user
abstraction; caller access (`ICallerAccess`, implemented by `CallerAccess`, which also implements Sessions'
`IParticipantAccess`)

**Status:** `GET /api/v1/me` (with `registeredAt`, null until registered) and `GET /api/v1/me/permissions`. A platform
user is registered when a participant creates a household (D-47) or, earlier, when they confirm their details with
`POST /api/v1/me/registration` (`RegisterCaller`, `{"detailsConfirmed": true}`; 201 created, 200 already registered;
audited as `identity.user.registered`). Registration stores the subject and the time only, never the details the
identity provider showed (D-97). Access is decided from the caller's household membership through the untracked
`IAccessQueries` (D-48, D-49). `POST /api/v1/me/sign-ins` and `/sign-outs` record session events
(`RecordSessionEvent`); `ObserveCallerRoles` keeps `role_snapshots` and audits each platform role a subject is seen to
gain or lose (D-81)

**Other modules may use:** `ICurrentUser`, `Roles`, `ICallerAccess`, `IAccessQueries`

**Must not:** Store identity-provider credentials or tokens; accept roles or user IDs from request payloads; save the
household snapshot `IAccessQueries` returns
