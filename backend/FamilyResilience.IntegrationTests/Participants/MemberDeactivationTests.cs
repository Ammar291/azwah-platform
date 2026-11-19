using System.Net;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Participants;

/// <summary>
/// Removing a member from the roster closes what they left open, in the same transaction: a session in progress is
/// abandoned (its answers stay as history and are never scored) and every assignment still waiting or in progress is
/// cancelled, each with its audit event. What they submitted stays as it is. The head here is 68, so the roster assigns
/// them module A (head of household) and module B (60 or over); the spouse manages the household.
/// </summary>
[Collection(ParticipantApiCollection.Name)]
public sealed class MemberDeactivationTests(ParticipantApi api)
{
    private ApiFactory Factory => api.Factory;

    private Task<Enrolment> EnrolAsync(CancellationToken cancellationToken) =>
        Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(68, self: false), Rosters.Spouse(self: true));

    private static Task<HttpResponseMessage> RemoveAsync(Enrolment enrolment, Guid memberId, CancellationToken cancellationToken) =>
        enrolment.Client.PatchJsonAsync(
            $"/api/v1/households/{enrolment.Household.Id}/members/{memberId}", new { status = "inactive" }, cancellationToken);

    private Task<List<AssessmentAssignment>> AssignmentsOfAsync(Guid memberId, CancellationToken cancellationToken) =>
        Factory.QueryAsync(db => db.AssessmentAssignments.AsNoTracking()
            .Where(assignment => assignment.FamilyMemberId == FamilyMemberId.From(memberId))
            .ToListAsync(cancellationToken));

    private Task<AssessmentSession> SessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Factory.QueryAsync(db => db.AssessmentSessions.AsNoTracking()
            .Include(session => session.Responses)
            .SingleAsync(session => session.Id == AssessmentSessionId.From(sessionId), cancellationToken));

    private Task<int> AuditCountAsync(string action, Guid resourceId, CancellationToken cancellationToken) =>
        Factory.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == action && audit.ResourceId == resourceId.ToString(), cancellationToken));

    private static Guid AssignmentIn(IEnumerable<AssessmentAssignment> assignments, ParticipantApi api, string sectionCode) =>
        assignments.Single(assignment => assignment.InstrumentSectionId == api.Section(sectionCode).Id).Id.Value;

    [Fact]
    public async Task Removing_a_member_abandons_their_session_in_progress_and_cancels_their_open_assignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var head = enrolment.Aged(68);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, head, "A", 0, cancellationToken);

        using var removed = await RemoveAsync(enrolment, head, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var session = await SessionAsync(sessionId, cancellationToken);
        Assert.Equal(
            (SessionStatus.Abandoned, (DateTimeOffset?)TestData.Now, (DateTimeOffset?)null),
            (session.Status, session.AbandonedAt, session.SubmittedAt));
        Assert.Equal(api.Section("A").Items.Count, session.Responses.Count);
        var assignments = await AssignmentsOfAsync(head, cancellationToken);
        Assert.Equal(2, assignments.Count);
        Assert.All(assignments, assignment => Assert.Equal(AssignmentStatus.Cancelled, assignment.Status));
        Assert.Equal(1, await AuditCountAsync("sessions.session.abandoned", sessionId, cancellationToken));
        Assert.Equal(1, await AuditCountAsync("assignments.assignment.cancelled", AssignmentIn(assignments, api, "A"), cancellationToken));
        Assert.Equal(1, await AuditCountAsync("assignments.assignment.cancelled", AssignmentIn(assignments, api, "B"), cancellationToken));
        Assert.Equal(1, await AuditCountAsync("households.member.deactivated", head, cancellationToken));
        Assert.Equal(0, await Factory.QueryAsync(db => db.ScoreResults.CountAsync(
            score => score.AssessmentSessionId == AssessmentSessionId.From(sessionId), cancellationToken)));
    }

    [Fact]
    public async Task Removing_a_member_keeps_what_they_submitted_and_cancels_only_what_is_still_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await EnrolAsync(cancellationToken);
        var head = enrolment.Aged(68);
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, head, "A", 1, cancellationToken);
        using var submitted = await Journeys.SubmitAsync(enrolment.Client, sessionId, Guid.NewGuid(), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        using var removed = await RemoveAsync(enrolment, head, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var session = await SessionAsync(sessionId, cancellationToken);
        Assert.Equal((SessionStatus.Scored, (DateTimeOffset?)TestData.Now, (DateTimeOffset?)null), (session.Status, session.SubmittedAt, session.AbandonedAt));
        var assignments = await AssignmentsOfAsync(head, cancellationToken);
        var moduleA = assignments.Single(assignment => assignment.Id.Value == AssignmentIn(assignments, api, "A"));
        var moduleB = assignments.Single(assignment => assignment.Id.Value == AssignmentIn(assignments, api, "B"));
        Assert.Equal((AssignmentStatus.Completed, AssignmentStatus.Cancelled), (moduleA.Status, moduleB.Status));
        Assert.Equal(0, await AuditCountAsync("sessions.session.abandoned", sessionId, cancellationToken));
        Assert.Equal(0, await AuditCountAsync("assignments.assignment.cancelled", moduleA.Id.Value, cancellationToken));
        Assert.Equal(1, await AuditCountAsync("assignments.assignment.cancelled", moduleB.Id.Value, cancellationToken));
    }

    // Closing what the member left open happens only when the removal itself succeeds: a refused removal changes nothing.
    [Fact]
    public async Task A_refused_removal_leaves_the_session_in_progress_and_its_assignment_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var enrolment = await Enrolment.CreateAsync(Factory, cancellationToken, Rosters.Head(68), Rosters.Spouse());
        var sessionId = await Journeys.AnsweredSessionAsync(enrolment, api, enrolment.Self, "A", 0, cancellationToken);

        using var removed = await RemoveAsync(enrolment, enrolment.Self, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, removed.StatusCode);
        Assert.Equal("households.member_is_manager", await removed.ProblemCodeAsync(cancellationToken));
        Assert.Equal(SessionStatus.InProgress, (await SessionAsync(sessionId, cancellationToken)).Status);
        var assignments = await AssignmentsOfAsync(enrolment.Self, cancellationToken);
        Assert.Equal(
            (AssignmentStatus.InProgress, AssignmentStatus.Assigned),
            (assignments.Single(assignment => assignment.Id.Value == AssignmentIn(assignments, api, "A")).Status,
                assignments.Single(assignment => assignment.Id.Value == AssignmentIn(assignments, api, "B")).Status));
        Assert.Equal(0, await AuditCountAsync("sessions.session.abandoned", sessionId, cancellationToken));
    }
}
