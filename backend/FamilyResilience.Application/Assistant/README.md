# Assistant module

**Owns:** the family context (`FamilyContextBuilder`, `FamilyContext` and its records); the Azwah agent (D-122):
what it is told (`AssistantPrompt`), its allow-listed tools (`IAssistantTool`: `SearchSupportDirectoryTool`,
`SuggestAppScreenTool`; run through `AssistantToolbox` as `IAssistantToolRunner`), its loop (`AssistantAgent`), the
output guard (`AssistantReplyGuard`), the conversation memory rules (`AssistantMemory`, D-123), the safety check port
(`IAssistantSafetyCheck`, default `NoAssistantSafetyCheck`, its `AssistantSafetyVerdict` and `AssistantSafetyVerdicts`)
and the metrics (`AssistantMetrics`); the text assistant (`GenerateAssistantReply` and its `AssistantReplyOutcome`,
`GetAssistantAvailability`, the model port `IAssistantModel` with its default `UnconfiguredAssistantModel`); the live
consultant (`StartConsultantSession`, `ConsultantStartMessage` and its
validator, the ports `IVoiceConsultantModel` and `IVoiceConsultantSession` with the default
`UnconfiguredVoiceConsultantModel`, `LiveConsultantSessions`, `ConsultantProtocol`; the call across model sessions,
`ContinuingConsultantSession`, with `ConsultantHistoryScreen` and the port `IConsultantCrisisReply`, default
`NoConsultantCrisisReply`, D-141); the written score report
(`GenerateScoreReport`, `ScoreReportPreparation`, `GetScoreReportEligibility`, `WriteScoreReportPart`,
`WriteScoreReportSummary`, `ScoreReportPrompt`, `ScoreReportFacts`, `ScoreReportWriting`, the port `IScoreReportModel`,
adapter `Infrastructure/Assistant/OpenAiScoreReportModel`, D-112, D-113); the support knowledge graph
(`ISupportKnowledgeGraph`, `SupportKnowledgeGraph`, `ListSupportAuthorities`, D-114); `AssistantScreens`,
`AssistantLimits` and `AssistantErrors`. It owns no table and has no migration; the API's relay for the live consultant
is `Api/Assistant/ConsultantRelay`

**Status:** The assistant, the consultant and the score report are connected to OpenAI when the API has a key in
`Assistant:Model:ApiKey`; they share the key, the settings record (`OpenAiSettings`) and the Responses transport
(`OpenAiResponses`), each with its own model. The score report writes with `OpenAiScoreReportModel`
(`Assistant:Model:ReportName`, default `gpt-5.4-mini`, D-116) and needs an approved, graded scoring rule set. It is
compact (D-145): the prompts give every text a word budget and `ScoreReportWordBudget` enforces it on what the model
wrote, at a sentence boundary; a whole report keeps two strengths, two weaknesses and the three weakest steps, a part one
of each with its area's `standing`, and each emirate one service. The assistant and consultant (D-122 to D-127 in
`docs/decisions.md`): the chat through
`Infrastructure/Assistant/OpenAiAssistantModel` (Responses API, `gpt-6.1-terra`, strict function tools), the safety
check through `OpenAiModerationCheck`, and the consultant through `OpenAiLiveConsultantModel` (GPT-Live 1 over a
server-side WebSocket, delegating to the same agent, D-124). Without a key, `GET /api/v1/assistant/availability` says
`{"assistant":false,"consultant":false,"scoreReport":false}`, `POST /api/v1/assistant/replies` answers 503 `assistant.unavailable`, and
`GET /api/v1/consultant/live` accepts the WebSocket, reads the app's start message, sends `{"type":"unavailable"}` and
closes normally. All three need the `Participant` policy; replies have the rate limit `assistant` (20 a minute), live
starts `consultant` (6 a minute). Each time a context is sent to a model the use case audits `assistant.reply.generated`
or `assistant.consultant.started` (resource type `household`, the household ID, the outcome; never text). The server
stores no conversation: the chat, the call lines and the memory live on the phone. A crisis signal in the person's chat
message or in what they say on a call raises a safety alert for staff, as fixed codes only; the reply carries the
crisis notice and the call gets the `safety` message (D-135, D-138). The live evaluation
(`backend/FamilyResilience.Evaluation`, D-127) checks the whole behaviour against the real models

**Other modules may use:** nothing

**Uses:** Identity's `ICallerAccess` and `ICurrentUser`; Sessions' `IParticipantAccess.IsHeadOfHouseholdAsync` and
`IAssessmentSessionRepository.ListForMembersAsync`; Consent's `ListMyConsents`; Assignments' `ListMyAssignments`;
Referrals' `ListMyRecommendations`; Questionnaires' `IInstrumentVersionRepository.GetAsync`; Scoring's
`IScoreResultRepository.ListForSessionAsync` (score results, read for the head only) and `ScoreScaleReader`;
SupportResources' `SearchSupportResources.SearchAsync` (the directory tool, never crisis-screened); Safeguarding's
`CrisisSignalScreen`, `ISafetySignalRecorder.RecordForCallerAsync` and `SafetyNotices`; Audit's `IAuditLog`

**Must not:** Build a context from anything the app sends but the language; include names, identity-provider details,
tokens or database IDs in a context (members are `member_1`, `member_2`, … in roster order); give score values to
anyone but the household's head (D-103); let a model's output become a score, band or eligibility decision, or act on
it (CONTRIBUTING.md rules 1 and 7); register a tool that takes a household, member or user identifier from its arguments or
reads beyond what the caller may read; store, log, audit or put in a metric a message, reply, caption, memory, tool
argument, audio or context (every record here prints counts only from `ToString()`); register a model adapter without
settings under `Assistant:Model` (secrets in user secrets) and the decision that approves it; let a deployed API run a
key without `Assistant:Model:ApprovalReference` (open question 52); pretend a model answers when none is configured

**Rules that hold:**

- **Context:** `FamilyContextBuilder.BuildAsync(language)` returns null for a caller with no active household
  membership (the model is then told `{"household":null}`). Otherwise it holds the caller (relationship, head flag,
  manager flag), the household (reference, status, roster confirmed), each member the caller may act for (D-49), their
  consents, questionnaires, sessions (every answered question's prompt and the chosen option's label in the language;
  abandoned sessions included), and next steps. Score results are read and included only when the caller signs in as
  the head; otherwise `results` is `head_of_household_only` and no result is read.
- **Agent loop (D-122):** prepare (instructions, the context and memory as data, the conversation with lines said on a
  call marked; the safety check and, at 30 or more messages, the memory refresh start alongside) → model (one
  `IAssistantModel.NextAsync` step with the tools) ⇄ tools (each call through the toolbox, its output handed back as
  data) → at most `AssistantAgent.MaxSteps` (4) steps, the last without tools → guard (`AssistantReplyGuard`: Markdown to
  plain text, empty if no letter or digit, 4,000 characters) → finish (the offered screens, SOS first and added when
  the safety check flags danger; the directory entries the reply names, at most 3; the new memory; the safety verdict).
  The side tasks get `AssistantAgent.SideTaskGrace` (2 s) after the reply and never fail the turn.
- **Safety verdict (D-126, D-136):** `NotChecked`, `Clear`, `DangerToSelf` (any self-harm category; it wins) or
  `DangerOfViolence` (violence or a threat at 0.6 or above); `IsDanger()` is true for the last two. `Concern()` maps
  them to `suicide_or_self_harm` and `abuse_or_danger`: moderation's violence categories cannot tell who is at risk, and
  a victim's "my husband hits me" must not be filed as harm to others.
- **Tools:** unknown tools, arguments that are not a JSON object and tool faults answer `{"error":<code>}`; calls run
  one at a time per request. `search_support_directory` clips its text to 200 characters, drops a category that is not
  a snake-case code and returns at most 5 entries in the person's language marked `information_only`. Its text is the
  model's words, not the person's, so it goes through `SearchSupportResources.SearchAsync`, which is not
  crisis-screened. `suggest_app_screen` accepts only `AssistantScreens` codes.
- **Replies:** 503 before anything is built when no model is configured. The validated chat (1–40 messages, each
  1–4,000 characters after trimming, 20,000 in all, the last from the person, each `chat` or `voice`; a memory of at most
  2,000 characters; `en` or `ar`) runs through the agent under `AssistantLimits.ModelTimeout` (30 s; `WaitAsync` bounds
  an adapter that ignores its token). A failure, an exception, an empty reply or a time-out is 502
  `assistant.model_failed`, with the exception dropped unlogged; only the caller's cancellation propagates. The audit is
  recorded either way. The response is `{reply, generatedAt, actions, sources, memory, safetyNotice}`.
- **Crisis signals (D-135, D-136, D-138):** before anything else, the model check included, the person's latest message
  goes through `CrisisSignalScreen`; a match starts recording at once (source `assistant_chat`, detector
  `phrase_screen`, the rule code, the chat's language), so a 503, a 502 or a person who closes the app still raises the
  alert. The recording runs beside the turn, so a slow database never holds the model back, and the reply waits for it
  before it returns. After the agent, a danger verdict is recorded as `ai_moderation` (`suicide_or_self_harm` or
  `abuse_or_danger`, no code) unless the phrase screen already recorded that same concern for the message. Recording is
  passed `CancellationToken.None`, never the request's token; the recorder bounds itself (5 seconds, a time-out logged
  and counted as a failure) and never fails the reply. When either detector fired, the reply has
  `safetyNotice: "crisis_support"` and SOS first in `actions` (the others keep their order, at most 3 in all), and a
  failure's ProblemDetails carries the extension `safetyNotice` beside `code` and `traceId`, even when recording failed.
  A server fault after a detector fired (the context, the caller lookup, the audit) is answered as a generic 500
  (`assistant.reply_faulted`) that still carries `safetyNotice`, logging only the exception's type; without a signal a
  fault propagates to the global handler as before. No text reaches the alert, a log, a metric or an audit event.
- **Live consultant (protocol 2, D-124):** the relay waits `AssistantLimits.ConsultantStartWait` (5 s) for the app's
  `start` message (validated by `ConsultantStartMessageValidator`: 0–40 messages within the chat's limits, a memory of
  at most 2,000 characters; otherwise `error bad_start`, close 1008). `StartConsultantSession` refuses with
  `assistant.unavailable` before claiming anything when no model is configured; otherwise it claims the subject's slot
  in `LiveConsultantSessions` (one live session per subject in this instance; a second start closes the first), builds
  the context, opens the model session with the memory, the earlier messages and the tools under the 30 s limit (a
  session that opens late is disposed at once), and audits the start. The relay enforces 15 minutes a session, 60
  seconds without audio from the app and 64 KB a frame, forwards audio, transcripts, `turn_end`, `interrupted` and
  `action` (known screen codes only), sends `safety` (below), ends a call whose model side fails (its socket included)
  as `model_failed`, and logs and counts only how a session ended.
- **Crisis signals on a call (D-135, D-136, D-138):** the relay (`Api/Assistant/ConsultantSafetyWatch`) screens each
  final line from the person (never the consultant's, never a growing line) with `CrisisSignalScreen`, and takes the
  session's `ConsultantSafetyFlag` as a signal too: the session emits one just before its SOS action, `ai_moderation`
  with the verdict's concern (`AssistantSafetyVerdicts.Concern`) or `provider_filter` with `suicide_or_self_harm` when
  OpenAI's content filter stops a model session after the person spoke. The flag is never sent to the app. The line the person was still saying when
  the call ended (it settles only when more words come) is held in memory until then and screened once nothing more is
  read; a match is recorded and nothing is sent. A concern is recorded at most once a call (source `consultant_voice`,
  the detector, the rule code for a phrase match, the call's language), on the thread pool so the relay of audio never
  waits for it, with `CancellationToken.None` so a hang-up still records; once the app's socket, the model session and
  the slot are closed, the request waits for what is still recording (at most the recorder's 5 seconds and 1 more)
  before it logs how the call ended. After the line or flag the app gets `{"type":"safety","notice":"crisis_support"}`
  once a call and, for a phrase match, the SOS action unless the call already offered it, whatever becomes of the
  recording. Protocol version 2 is unchanged: the app ignores types it does not know. No text reaches the alert, a
  log, a metric or an audit event.
- **Adapters** live in Infrastructure, are registered over the defaults and say they are not configured without a key,
  are truly asynchronous and honour their token, and log at most a fixed code (never OpenAI's error text). The chat and
  the score report post through the one `OpenAiResponses` transport on the one shared HttpClient: a dropped connection
  is tried once more; the chat also retries a 429 or 5xx once after a short pause, while the report's answer of any
  status is final (a part's call must beat the person's next tap). Both OpenAI connections race their addresses IPv4
  first (`HappyEyeballs`); the live WebSocket also hedges its connect after 3 s.
- **GPT-Live:** PCM 24 kHz both ways; silent frames are not relayed; transcript deltas become growing lines that
  settle when the other speaker starts, after a 1.5 s gap, after about a second of the consultant's silence, or at the
  end; a delegated function call runs off the audio path and its output goes back with `response.item.create`, then
  `response.create` once that response completes (neither takes `delegation_id`); each final line from the person goes
  to the safety check, and danger raises an `ai_moderation` flag and offers the SOS screen. OpenAI's `content_filter`
  error, or `session.closed` with reason `content`, ends the session with `ConsultantContentStopped`: the person's open
  line is settled, the consultant's cut line dropped, and nothing more is relayed or sent. A session can be started to
  listen (`ConsultantOpening.Listen`) with a note after the history (a developer message), and asked to say Azwah's
  words (`SpeakAsync`: the words as `session.instructions.append`, then `session.commentary.append` with
  `AssistantPrompt.SpeakNow`, because an instruction alone was sometimes held until the person spoke). An error with no
  fixed code is logged by its type (`error_type_server_error`), never by its message.
- **A call outlives the sessions the filter stops (D-141):** `ContinuingConsultantSession` wraps the call's model
  sessions. A stop after the person spoke in that session emits the `provider_filter` flag, SOS, `interrupted` and an
  empty final consultant line (the app hides and never saves it), then opens a session that listens with
  `AssistantPrompt.CrisisResumeNote` and the call's screened history and memory, and has it say the reply
  `IConsultantCrisisReply` writes from the person's latest line (`Infrastructure/Assistant/OpenAiCrisisReply`,
  `Assistant:Model:CrisisName`, default `gpt-5.4-mini`, reasoning `none`), or `AssistantPrompt.CrisisFallbackWords` when
  it is not written within `AssistantLimits.ConsultantCrisisReplyWait` (5 s). Microphone audio is dropped while no session
  listens. At most `ConsultantResumeLimit` (5) carry-ons a call; the next stop has a last session say
  `CrisisClosingWords`, unheard, and the call ends (`model_ended`). A stop before the person spoke in that session (or
  the stream failing then, in a session that carries the call on) raises nothing and reopens once without the earlier
  conversation and the memory; after a carry-on it keeps the note, listens, and says the fixed words if the support was
  never heard. A second such stop, a session that cannot be reopened within 30 s, or any other fault ends the call as
  `model_failed`. A frame that fails as the provider drops its socket waits up to 1 s for the stop before it counts.
  Counted as `azwah.consultant.resumes` (`person`, `seeded`, `fault`, `limit`) and `model_calls` kind `crisis`; logged
  as those codes only.
- **Earlier conversation (D-141):** `StartConsultantSession` prepares the first request (the context, and
  `ConsultantHistoryScreen` on the earlier lines) while the model connects (`IVoiceConsultantModel.OpenAsync` taking the
  request still being prepared; the OpenAI adapter opens its WebSocket first): every start message, the person's or the
  assistant's, through the phrase screen, then the rest in one batched safety check
  (`IAssistantSafetyCheck.CheckManyAsync`) for at most `ConsultantHistoryCheckWait` (2.5 s). Lines either flags are
  held back and replaced by `AssistantPrompt.WithheldHistoryNote`; they raise nothing.
- **Score report:** 503 before anything is read when no model is configured; 404 for a session the caller may not act
  for; 403 for anyone but the head; 409 without a complete score on a graded scale. The facts hold no roster, ages,
  names, household reference or IDs. Text outside `ScoreReportWriting`'s limits counts as a model failure (502). Each
  send is audited, also when the person leaves while the model writes. The total, band and scale in the response come
  from the record, never from the model.
- **Writing ahead (D-113):** `ScoreReportPreparation` holds the shared checks, adding active participation consent and an
  approved scoring rule set with bands covering the session; `GetScoreReportEligibility` turns them into yes or no.
  `WriteScoreReportPart` and `WriteScoreReportSummary` work from the server's own answers, need the section (or all of
  them) answered (409), and hold no total or band. Rate limit `score-report-parts`, 120 an hour; 30 s a call.
- **Next steps (D-114, D-115):** `ISupportKnowledgeGraph.Infer` walks from a family's answers through needs to services,
  deterministically. The model gets those options and picks by code; `ScoreReportNextStep.Choose` drops any code it wasn't
  offered and falls back to the graph's order. Names, procedures and contacts come word for word from the graph, and a model
  never sees a procedure or contact. `standing` orders the steps and is not shown. `ListSupportAuthorities` serves All
  services, the same for everyone. How to change it: `docs/support-knowledge-graph.md`.
