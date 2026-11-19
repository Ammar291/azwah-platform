# Scoring module

**Owns:** `ScoreResult`, `ScoreContribution` and `ScoreCategoryScore`; `IScoreResultRepository`; `SessionScoringService`;
the staff scoring use cases (`ScoreSession`, `ListScoreResults`) and the household results list for system
administrators (`ListHouseholdResults`, `IHouseholdResultReadModel`, D-104); the family's resilience over time for the
participant app (`GetResilienceTrend`, `ResilienceTrend`, `IResilienceTrendReadModel`, `ScoreScaleReader`, D-132);
validation of `scoring.v1` definitions; `DemoAnswerPlanner`, answers the approved rule set scores in a chosen band, for
demonstrations only (D-130), sharing `ApprovedScoringPolicy` with scoring, and `DemoAnswerPlanCache`, its checked plans
kept per approved rule set, scope and level (D-143). The strategies live in `Domain/Scoring`.

**Status:** Submission scores a session automatically with the single approved scoring rule set for its scope (D-52);
staff can score a session submitted before its rule set was approved, which also routes it. Staff responses carry
`contributionCount`, never per-item contributions (D-48). No scoring rule set is approved, so no real session is scored,
and `GET /api/v1/me/resilience-trend` has nothing to draw for a real family yet (D-132)

**Other modules may use:** `SessionScoringService` inside a transaction; score results; `IScoreResultFacts` (a recorded
result's `RuleSetCode`, the code of the scoring rule set that produced it, not the strategy code; its status, tier and
session; and the session's assignment member and their household; no total or answers), which Safeguarding reads when
`scoring.score_result_recorded.v1` is delivered; `ScoreScaleReader`, the scale of a rule set's total (the trend chart,
D-132) and the grading scale of a recorded result or of the approved rule set covering a session (D-111);
`DemoAnswerPlanner` (Sessions' demonstration tools only, D-130)

**Must not:** Use AI or statistical models to score; use unapproved rule sets; combine the two instruments; score from a
rule set whose scope has `Unspecified` directions; edit a recorded result; return per-item answers to staff; join trend
points scored by different rule sets, or draw a total against a scale it was not scored on
