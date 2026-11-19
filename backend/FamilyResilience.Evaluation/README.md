# Live evaluation of the Azwah assistant and consultant (D-127)

A console app that drives the **real** OpenAI adapters (`OpenAiAssistantModel`, `OpenAiModerationCheck`,
`OpenAiLiveConsultantModel`) with synthetic families (`SyntheticFamilies`), scripted multi-turn chats and calls
(`Scenarios`) and synthetic speech (OpenAI text-to-speech, cached under the temporary folder). Each turn is checked by
fixed rules and, where a rule cannot judge, by an LLM judge with one rubric (`Judge`, `gpt-6.1-sol` unless
`AZWAH_JUDGE_MODEL` says otherwise). The report lists every turn, what was said back, the checks and the latency, with
medians and 90th percentiles.

Nothing references this project and the API image never contains it (`DependencyRulesTests`). It uses no participant
data: the families, the directory entries and the speech are synthetic. It needs `OPENAI_API_KEY` (the environment or
the nearest `.env` above the working folder) and costs money (a full run is a few cents of chat and about five minutes of
GPT-Live), so run it when you change a prompt, a model, a tool or the agent, not on every build.

```
dotnet run --project backend/FamilyResilience.Evaluation -- --suite all --out <report.md>
dotnet run --project backend/FamilyResilience.Evaluation -- --suite voice --only "SOS"
```

`--suite` is `chat`, `voice` or `all`; `--only` keeps the scenarios whose name contains the text; `--min-pass` (default
0.85) sets the pass rate below which the exit code is 1. Warnings the adapters log (fixed codes only) are printed.

What the scenarios cover: memory within a chat, across a call and from the rolling memory; finding approved help (the
directory tool, the cards) instead of deflecting; no invented organisations or numbers; results reported as recorded,
bands not explained, no predictions or eligibility; results only for the head of household; SOS and the crisis notice
on danger (as the server decides the notice: the phrase screen or the safety check, D-138) and no SOS on ordinary
worries; offering the right screen; prompt injection; off-topic requests; Arabic; plain text; and on calls: greeting,
answers from the family's records, delegated directory search, band rules, memory of the chat, SOS and the safety
message, Arabic, and the consultant's own voice mixed back into the microphone at 30 %.

Calls run as the API runs them (D-141): the earlier conversation goes through `ConsultantHistoryScreen` and the call
through `ContinuingConsultantSession` with the real supportive-reply writer (`OpenAiCrisisReply`). The crisis
scenarios check that a call about self-harm gets SOS and the notice, hears support after OpenAI's filter stops the
session, and carries on (twice in English, once in Arabic); two more check that earlier lines that would trip the filter
are held back, and that a call stopped by its unscreened earlier conversation reopens without it. For each stop the
report notes how long after the person stopped it came, how much of the consultant's own reply was heard first, when
the new session opened and was asked to speak, how long the reply took to write, and when the support was heard. The
console prints every `error` and `session.closed` event OpenAI sends, and socket failures, with their messages (the
calls are synthetic; the API itself logs fixed codes only).
