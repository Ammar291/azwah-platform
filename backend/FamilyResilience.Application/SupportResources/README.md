# SupportResources module

**Owns:** `SupportResource` (the versioned directory entry, `ISupportResourceRepository`), `SupportResourceContent` and
its rules, and the administration use cases: `CreateSupportResourceDraft`, `UpdateSupportResourceDraft`,
`ApproveSupportResource`, `RetireSupportResource`, `ListSupportResources`, `GetSupportResource`; the reader use cases
`SearchSupportResources`, `ListPublishedSupportResources` and `GetPublishedSupportResource`; `ISupportResourceSearch`
(the deterministic read model), `ISupportResourceAssistant` (the deterministic assistant and the dormant
`LanguageModelSupportResourceAssistant`), `LanguageModelSettings`, the provider port `ILanguageModelClient` with its
prompt, redactor and output guard, and `SupportResourceMetrics`

**Status:** System administrators keep the directory (`/api/v1/admin/support-resources`, policy `SystemAdministrator`,
permission `support_resources.manage`): they draft a version, edit it while it is a draft, approve it with a named
source and a review date (at most 366 days away), and retire it. Approving a version retires the code's previous
approved version in the same transaction. Every change is audited (`support_resources.resource.drafted`, `…updated`,
`…approved`, `…retired`). An entry is discoverable only while approved and before its review date. The directory is a
list of links to organisations outside the platform; nothing is connected to them. Participants, support coordinators
and system administrators search it (`POST /api/v1/support-resources/search`, policy `SupportResourceReader`,
permission `support_resources.read`, rate limit `support-searches`), list it a page at a time ordered by code
(`GET /api/v1/support-resources?page=1&pageSize=20`, the same filters, for the app's All services place) and open one
entry (`GET /api/v1/support-resources/{resourceId}`). The search is deterministic PostgreSQL full-text search over
approved, in-date entries; staff roles also see `StaffOnly` entries, decided from the token's roles. Every answer
carries the notice `information_only`. A language-model path exists but is dormant: it is off by default
(`SupportResources:LanguageModel:Enabled` is false), no provider is approved or configured, and no
`ILanguageModelClient` adapter ships, so the assistant is the deterministic one. Search text is never stored, logged,
put in a metric tag or put in a URL, and no search or read is audited. A participant's search text goes through
Safeguarding's crisis phrase screen first (D-135, D-138; staff searches never do): a match raises a safety alert for
staff (source `support_search`, detector `phrase_screen`, the rule code, no language; codes only, audited by
Safeguarding; recorded with `CancellationToken.None`, never the request's token, and bounded by the recorder; started
beside the search and waited for before the answer), and the answer carries `safetyNotice: "crisis_support"` (null
otherwise). A server fault after a match is answered as a generic 500 (`support_resources.search_faulted`) whose
ProblemDetails still carries the extension `safetyNotice`, logging only the exception's type; without a match a fault
propagates to the global handler as before. The search itself is the same either way. As built:
[`docs/support-resource-discovery.md`](../../../docs/support-resource-discovery.md) (D-92 to D-95)

**Other modules may use:** the Assistant's `search_support_directory` tool calls `SearchSupportResources.SearchAsync`
(D-122), with the caller's own roles: the same search, never crisis-screened, because its text is the model's words, not
the person's; nothing else. Any other consumer uses the read-only `ISupportResourceSearch`, never the repository or its
tracked aggregates. The only shared vocabulary is the snake-case support category code, as a plain string

**Uses:** Safeguarding's `CrisisSignalScreen`, `ISafetySignalRecorder.RecordForCallerAsync` and `SafetyNotices`;
Identity's `Roles`

**Must not:** Reference Sessions, Scoring, Referrals, Assignments, Households, Members, Consent, Questionnaires,
Analytics or Privacy; store, log or audit search text, or put it in a metric tag or a URL; approve an entry without
Arabic text, a named approver, a source and a review date; change an approved version (draft a new one instead);
register an `ILanguageModelClient` or switch the model on without the prerequisites in D-94 (spec §11); let a model
write text a family sees

**Rules that hold:**

- **Approving:** inside one `ExecuteInTransactionAsync`, load the draft and refuse unless it is a draft, retire the
  code's approved version (stage its audit, commit), then approve (stage its audit, commit). The partial unique index
  `ux_support_resources_one_approved_per_code` is checked per statement, so the two changes need two commits. A failure
  rolls both back.
- **Drafting:** the next version number is the highest used for the code plus one; a race ends in
  `support_resources.version_number_taken` (409) from the unique index.
- **Language model (dormant):** `AddApplication` chooses `LanguageModelSupportResourceAssistant` only when
  `LanguageModelSettings.Enabled` is true; otherwise the deterministic assistant answers. Switched on, the API refuses
  to start (`LanguageModelStartupGuard`, any environment) unless `ApprovalReference` names the approving decision (at
  most 100 characters), an `ILanguageModelClient` is registered, and every value is in range (timeout 1–30 s, attempts
  1–3, candidates 1–20, output tokens 64–1024); the error names codes only. The model **selects and orders**, never
  writes: without words it is not asked; candidates come from the same search with coverage relaxed to any term (at
  most `MaxCandidates`, with the caller's visibilities, so staff-only entries never reach a participant's request);
  none means `no_match` without a call. It receives the fixed `support_resource_selection.v1` instructions and one
  JSON document holding the redacted text (no emails, links, Emirates IDs or digits) and candidates under handles
  `r1…rN` (never IDs, codes or URLs). Each attempt has its own timeout; only timeouts and transient faults are retried,
  up to `MaxAttempts` in total. The strict guard accepts only `{"selected":[known handles]}` within the limit and never
  throws, and matches are re-rendered from directory rows in the model's order. Every failure answers with the strict
  deterministic search, `mode: deterministic` and a fallback code (`model_timeout`, `model_unavailable`,
  `model_output_invalid`, `model_output_ungrounded`, and `model_selected_none` for an empty selection); only the
  caller's cancellation propagates. Attempts and tokens are counted by fixed codes; nothing is logged.
- **A future adapter** (only with D-94's prerequisites) lives in Infrastructure, must be truly asynchronous (a client
  that blocks before returning its task cannot be cut off by the timeout), and may log at most a fixed category and an
  exception type name, never a prompt, reply or query. `LanguageModelRequest` and `LanguageModelReply` print only
  lengths and token counts from `ToString()`, but their properties hold the redacted text, the candidates and the
  reply, so they must never be logged.
