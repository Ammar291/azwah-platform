# Safeguarding module

**Owns:** `SafetyAlert` (`safety_alerts`): a durable record that a person may be in danger or at risk, for staff to
acknowledge and resolve; the deterministic bilingual crisis phrase list (`crisis-signals.v1.json`, an embedded
resource) and its loader `CrisisSignalCatalog`; the signal recorder `RecordSafetySignal`; the admin use cases that list,
count, acknowledge and resolve alerts (`ListSafetyAlerts`, `GetSafetyAlertSummary`, `AcknowledgeSafetyAlert`,
`ResolveSafetyAlert`, served at `/api/v1/admin/safety-alerts` to system administrators only); `SafeguardingMetrics`
(meter `FamilyResilience.Safeguarding`).

**Status:** Domain types and the phrase screen are built (D-135, D-136), and so are persistence (the `safety_alerts`
table and its guard trigger, `ISafetyAlertRepository`, the staff read model `ISafetyAlertReadModel`, household erasure),
the recorder and the admin API. The assistant chat (`GenerateAssistantReply`: the phrase screen and the moderation
verdict), the live consultant (`Api/Assistant/ConsultantSafetyWatch`: the phrase screen on each final line from the
person and on the line they were still saying when the call ended, the moderation verdict and OpenAI's content filter)
and the directory search (`SearchSupportResources`,
participants only) call the recorder. So does the score hook, `ScoreResultRecordedSafetyHandler`, an outbox handler for
`scoring.score_result_recorded.v1`: a complete result whose tier `ScoreAlertSettings` lists raises `low_resilience_score`
about the assessed member and their household, with detector `score_band` and the tier as signal code, through
`RecordForSessionAsync`; a failed recording is thrown, so the outbox retries. `Safeguarding:ScoreAlerts:Tiers` is keyed
by the scoring rule set's code (`IScoreResultFacts.RuleSetCode`, not the strategy code); `appsettings.json` holds the
default `household_assessment_total` → `["vulnerable"]`, and an environment replaces a tier by its index
(`Safeguarding__ScoreAlerts__Tiers__household_assessment_total__0=<tier>`). The off switch is
`Safeguarding:ScoreAlerts:Enabled=false` (default `true`): the handler then completes every message and records nothing.
An `Enabled` value that is not `true` or `false`, or a rule-set code or tier that is not snake case, stops the API from
starting (`Api/Safeguarding/ScoreAlertStartupGuard`).

**Other modules may use:**

- `ISafetySignalRecorder`: `RecordForCallerAsync` (the subject is the signed-in caller: their user, and their household
  and own member when they have one, from `ICallerAccess` resolved in each attempt's own DI scope, never the
  request's; skipped when the server has no record of the caller) and `RecordForSessionAsync` (a score signal for a
  session, with the subject from the server's records; skipped once the session has an alert in any status). It takes
  fixed codes only (`SafetySignal`). A repeat of an unresolved alert for the same user, concern and source adds to its
  count instead of raising another, and re-opens an acknowledged alert (status `open`, the acknowledgement cleared; who
  acknowledged it stays in the audit trail), so a repeat pages staff again. `safeguarding.alert.raised` and
  `safeguarding.alert.repeated` are audited as the System actor (`AuditRecord.BySystem`), never the participant: the
  detector acted, and no trail of crisis signals under the person's subject outlives the erasure of their alerts. Staff
  actions (`safeguarding.alerts.read`, `.acknowledged`, `.resolved`) name the staff member. It saves in a DI scope of
  its own, so it never commits or spoils the caller's pending changes; a lost race (the same alert inserted or changed
  first by another request) is read again once and folded into the alert that won. It bounds itself: one recording has
  `RecordSafetySignal.RecordingLimit` (5 seconds, from the injected `TimeProvider`) in all, linked to the caller's token.
  It never throws into its caller except for the caller's own cancellation: a fault, the budget running out included, is
  logged as the fixed code `safety_alert_failed` with the exception's type name or the refused error's code (never a
  message), counted as `azwah.safeguarding.recording_failures` and returned as `Failed`. New alerts are counted as
  `azwah.safeguarding.alerts_raised` and repeats as `azwah.safeguarding.alerts_repeated` (tags `concern`, `severity`,
  `source`); a caller's signal skipped because the server has no record of the caller is counted as
  `azwah.safeguarding.recording_skipped` (tag `source`). Participant-input hooks pass `CancellationToken.None`, so a
  person who sends a crisis message and closes the app still raises an alert; the chat and the search start the
  recording beside their work and wait for it before answering. Never call it inside an explicit transaction that locks
  or creates the subject's rows: its own scope would wait on that lock until the budget ran out.
- `SafetyNotices.CrisisSupport` (`crisis_support`): the notice a response carries when a crisis signal was detected.
- `CrisisSignalScreen` (a singleton built from `CrisisSignalCatalog.Load()` at start-up) and `CrisisTextNormalizer`
  (Domain) screen text for crisis phrases: whole words in order; the first Arabic word may carry one clitic (و, ف, ب or
  س) or و/ف then ب/س when three letters remain; a rule's `except` phrases cancel the occurrences they share a word with
  (`hits me up`).

**Must not:** Store, log, audit, tag or return what the person said (alerts hold fixed codes only, and no ID reaches a
log); use AI to score or decide anything about the family (an AI moderation verdict only flags text for a human); expose
alerts to anyone but a system administrator (a score alert reveals a score, D-62); treat the phrase list as clinically
reviewed (open question 58).
