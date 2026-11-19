# Assignments module

**Owns:** `Wave`, `AssessmentAssignment`; `IWaveRepository`, `IAssessmentAssignmentRepository`; `IHouseholdRunLock`
(implemented in Infrastructure by `HouseholdRunLock`, which locks the household row so assignment runs for one household
queue; D-44 explains why); the eligibility service `IHouseholdAssignmentService`; `IAssignmentReadModel`

**Status:** The eligibility evaluator, the assignment planner and the staff endpoints (`evaluate-eligibility`, `assign`)
are built. Roster confirmation runs the same `HouseholdAssignmentService` for the current wave (D-50). Participants list
and read their assignments, without reason codes. Researchers and program administrators list waves for the dashboard's
filters (`GET /api/v1/admin/waves`, D-74). No eligibility rule set is approved, so nothing is assigned outside test
databases. An assignment expires when its wave closes: from then on nothing is started, saved or submitted on it
(`assignments.assignment_expired`), and the maintenance service marks it Expired (Sessions' `ExpireOverdueAssignments`).
Removing a member cancels their assignments still waiting or in progress (Sessions' `IMemberAssessmentCloser`). A
participant's assignment says which instrument kind it answers (`instrumentKind`, D-146). Demonstration tools only
(D-144, `DemoTools:Enabled`): `OpenDemoCheckIn` (`POST /api/v1/assignments/demo-check-in`) makes a wave for the
caller's household that is never opened and runs `IHouseholdAssignmentService` for that household alone, or returns its
household assessment still open; audited as `assignments.demo_check_in.opened`

**Other modules may use:** `IHouseholdAssignmentService` inside a transaction; assignments for the caller's household
through `IAssignmentReadModel`

**Must not:** Evaluate eligibility in a client or from client-supplied facts; open a wave from the demonstration
check-in (another open wave would stop every roster confirmation); assign without an approved eligibility
rule set; assign members under 18; assign from an unapproved or unparseable rule set; assign to members of a withdrawn
household (open question 25: until the domain enforces it, handlers must check the household status); show eligibility
reason codes to participants
