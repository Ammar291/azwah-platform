# Sessions module

**Owns:** `AssessmentSession` with its responses; `IAssessmentSessionRepository`; `ISessionLock`; `ISessionReadModel`;
the session use cases

**Status:** Participant endpoints: start a session, save answers in batches, read them back, submit, read the result,
and the caller's assessment history. Starting, answering and submitting need an active household and active
participation consent. Submission locks the session and commits the submission, the score and the routing decisions
in order, in one transaction, with an `Idempotency-Key` (D-52, D-54). The result and the history carry score values
for the household's head only (`ScoreVisibility`, D-103), and the result their grading scale (D-111). `AbandonSession`
has no endpoint yet. Once an assignment's
expiry (its wave's close) has passed, starting, saving answers and submitting answer `assignments.assignment_expired`;
the maintenance service then runs `ExpireOverdueAssignments`, which abandons a session still in progress (its answers
stay as history and are never scored) and marks the assignment Expired, each with its audit event (at most 500 closed
a run; one that cannot be closed is passed over for the rest of the run and tried again at the next). Removing a member
from the roster runs `IMemberAssessmentCloser` in the same transaction: their sessions in progress are abandoned and
their assignments still waiting or in progress are cancelled, each with its audit event. Demonstration tools (D-130,
`DemoTools:Enabled`, Development only): `PlanDemoAnswers` returns answers for a low, medium or high result, chosen by
Scoring's `DemoAnswerPlanner`, and saves nothing; `SubmitDemoAnswers` (D-143, `POST …/demo-submission`) chooses them the
same way, saves them through `SaveResponses` and submits through `SubmitSession`, so they are refused, locked, committed
and scored exactly as a person's answers are (audited as `sessions.demo_answers.saved`)

**Other modules may use:** Submitted sessions; `IMemberAssessmentCloser` inside a transaction, after the change that
removes the member is committed; `DemoToolsSettings` and `DemoToolsErrors` (Assignments' demonstration check-in, D-144)

**Must not:** Compute scores (Scoring does); answer and submit in the same commit; accept free-text answers; leave a
session Submitted when its scoring or routing failed; accept an answer or a submission after the assignment's expiry
