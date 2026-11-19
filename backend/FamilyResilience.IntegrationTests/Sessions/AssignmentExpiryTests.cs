using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Sessions;

/// <summary>
/// What happens once an assignment's time has passed. The fixture's clock stands at <see cref="TestData.Now"/>, so the
/// overdue assignments here were assigned twelve hours earlier with an expiry one hour ago.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AssignmentExpiryTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset AssignedAt = TestData.Now.AddHours(-12);
    private static readonly DateTimeOffset StartedAt = TestData.Now.AddHours(-6);
    private static readonly DateTimeOffset AnsweredAt = TestData.Now.AddHours(-5);

    private async Task<T> WithAccessAsync<TUseCase, T>(Func<TUseCase, Task<T>> call)
        where TUseCase : class
    {
        await using var scope = fixture.CreateScope();
        return await call(ActivatorUtilities.CreateInstance<TUseCase>(scope.ServiceProvider, new AllowAllParticipantAccess()));
    }

    private async Task<OverdueAssignmentsReport> RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        await using var scope = fixture.CreateScope();
        return TestData.Ok(await scope.ServiceProvider.GetRequiredService<ExpireOverdueAssignments>().HandleAsync(cancellationToken));
    }

    /// <summary>A consenting member's assignment that expires at <paramref name="expiresAt"/> (an hour ago unless given).</summary>
    private async Task<(AssessmentAssignment Assignment, InstrumentVersion Version)> AssignedAsync(
        CancellationToken cancellationToken, DateTimeOffset? expiresAt = null)
    {
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        await fixture.GrantedConsentAsync(member, await fixture.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken), cancellationToken);
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var wave = await fixture.OpenWaveAsync(cancellationToken);
        var rules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Eligibility, version, cancellationToken);
        var assignment = TestData.Ok(AssessmentAssignment.Assign(
            member, version, null, wave, rules, AssignmentReason.EligibilityRule, expiresAt ?? TestData.Now.AddHours(-1), AssignedAt));
        await fixture.InScopeAsync(
            services =>
            {
                services.GetRequiredService<IAssessmentAssignmentRepository>().Add(assignment);
                return Task.CompletedTask;
            },
            cancellationToken);
        return (assignment, version);
    }

    /// <summary>The same, with a session started before the expiry and its first item answered.</summary>
    private async Task<(AssessmentAssignment Assignment, InstrumentVersion Version, AssessmentSessionId SessionId)> StartedAsync(
        CancellationToken cancellationToken, bool answerEverything = false)
    {
        var (assignment, version) = await AssignedAsync(cancellationToken);
        var session = await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, StartedAt));
                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started;
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                foreach (var item in version.GetItems().Take(answerEverything ? int.MaxValue : 1))
                {
                    TestData.Ok(tracked.Answer(version, item.Id, item.Options[0].Id, AnsweredAt));
                }
            },
            cancellationToken);
        return (assignment, version, session.Id);
    }

    private Task<AssessmentAssignment> SavedAssignmentAsync(AssessmentAssignmentId id, CancellationToken cancellationToken) =>
        fixture.QueryAsync(db => db.AssessmentAssignments.AsNoTracking().SingleAsync(candidate => candidate.Id == id, cancellationToken));

    private Task<AssessmentSession> SavedSessionAsync(AssessmentSessionId id, CancellationToken cancellationToken) =>
        fixture.QueryAsync(db => db.AssessmentSessions.AsNoTracking()
            .Include(candidate => candidate.Responses)
            .SingleAsync(candidate => candidate.Id == id, cancellationToken));

    private Task<int> AuditCountAsync(string action, Guid resourceId, CancellationToken cancellationToken) =>
        fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == action && audit.ResourceId == resourceId.ToString(), cancellationToken));

    private static SaveResponsesRequest Answers(InstrumentVersion version, int optionIndex) =>
        new(version.GetItems().Select(item => new AnswerRequest(item.Id.Value, item.Options[optionIndex].Id.Value)).ToList());

    [Fact]
    public async Task Saving_answers_after_the_assignment_expired_is_refused_and_saves_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, version, sessionId) = await StartedAsync(cancellationToken);

        var refused = await WithAccessAsync<SaveResponses, Result<SessionAnswersResponse>>(
            useCase => useCase.HandleAsync(sessionId.Value, Answers(version, 1), cancellationToken));

        Assert.Equal(AssignmentErrors.AssignmentExpired, refused.Error);
        var session = await SavedSessionAsync(sessionId, cancellationToken);
        Assert.Equal(SessionStatus.InProgress, session.Status);
        var kept = Assert.Single(session.Responses);
        Assert.Equal((version.GetItems().First().Options[0].Id, AnsweredAt), (kept.ResponseOptionId, kept.AnsweredAt));
        Assert.Equal(AssignmentStatus.InProgress, (await SavedAssignmentAsync(assignment.Id, cancellationToken)).Status);
    }

    [Fact]
    public async Task Submitting_after_the_assignment_expired_is_refused_and_nothing_is_scored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, version, sessionId) = await StartedAsync(cancellationToken, answerEverything: true);
        await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken);

        var refused = await WithAccessAsync<SubmitSession, Result<SubmissionResponse>>(
            useCase => useCase.HandleAsync(sessionId.Value, Guid.NewGuid(), cancellationToken));

        Assert.Equal(AssignmentErrors.AssignmentExpired, refused.Error);
        var session = await SavedSessionAsync(sessionId, cancellationToken);
        Assert.Equal((SessionStatus.InProgress, (DateTimeOffset?)null), (session.Status, session.SubmittedAt));
        Assert.Equal(AssignmentStatus.InProgress, (await SavedAssignmentAsync(assignment.Id, cancellationToken)).Status);
        Assert.Equal(0, await fixture.QueryAsync(db => db.ScoreResults.CountAsync(score => score.AssessmentSessionId == sessionId, cancellationToken)));
        Assert.Equal(0, await AuditCountAsync("sessions.session.submitted", sessionId.Value, cancellationToken));
    }

    [Fact]
    public async Task Maintenance_expires_an_overdue_assignment_that_was_never_started()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, _) = await AssignedAsync(cancellationToken);

        var report = await RunMaintenanceAsync(cancellationToken);

        Assert.True(report.AssignmentsExpired >= 1);
        Assert.Empty(report.Failures);
        var saved = await SavedAssignmentAsync(assignment.Id, cancellationToken);
        Assert.Equal((AssignmentStatus.Expired, TestData.Now, (DateTimeOffset?)null), (saved.Status, saved.UpdatedAt, saved.CompletedAt));
        Assert.Equal(1, await AuditCountAsync("assignments.assignment.expired", assignment.Id.Value, cancellationToken));
        var start = await WithAccessAsync<StartSession, Result<SessionResponse>>(
            useCase => useCase.HandleAsync(assignment.Id.Value, cancellationToken));
        Assert.Equal(AssignmentErrors.AssignmentExpired, start.Error);
    }

    [Fact]
    public async Task Maintenance_abandons_an_overdue_session_and_expires_its_assignment_keeping_the_answers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, version, sessionId) = await StartedAsync(cancellationToken);

        var report = await RunMaintenanceAsync(cancellationToken);

        Assert.True(report.SessionsAbandoned >= 1);
        Assert.True(report.AssignmentsExpired >= report.SessionsAbandoned);
        Assert.Empty(report.Failures);
        var session = await SavedSessionAsync(sessionId, cancellationToken);
        Assert.Equal(
            (SessionStatus.Abandoned, (DateTimeOffset?)TestData.Now, (DateTimeOffset?)null),
            (session.Status, session.AbandonedAt, session.SubmittedAt));
        Assert.Single(session.Responses);
        Assert.Equal(AssignmentStatus.Expired, (await SavedAssignmentAsync(assignment.Id, cancellationToken)).Status);
        Assert.Equal(1, await AuditCountAsync("sessions.session.abandoned", sessionId.Value, cancellationToken));
        Assert.Equal(1, await AuditCountAsync("assignments.assignment.expired", assignment.Id.Value, cancellationToken));

        // The participant is still told the time has passed, not that the questionnaire was submitted.
        var save = await WithAccessAsync<SaveResponses, Result<SessionAnswersResponse>>(
            useCase => useCase.HandleAsync(sessionId.Value, Answers(version, 1), cancellationToken));
        var submit = await WithAccessAsync<SubmitSession, Result<SubmissionResponse>>(
            useCase => useCase.HandleAsync(sessionId.Value, Guid.NewGuid(), cancellationToken));
        Assert.Equal(AssignmentErrors.AssignmentExpired, save.Error);
        Assert.Equal(AssignmentErrors.AssignmentExpired, submit.Error);
    }

    [Fact]
    public async Task Maintenance_leaves_assignments_alone_that_are_not_overdue_or_were_completed_in_time()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (waiting, _) = await AssignedAsync(cancellationToken, TestData.Now.AddMinutes(1));
        var (completed, version, sessionId) = await StartedAsync(cancellationToken, answerEverything: true);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var assignment = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(completed.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                TestData.Ok(session.Submit(version, assignment, TestData.Now.AddHours(-4)));
            },
            cancellationToken);

        await RunMaintenanceAsync(cancellationToken);

        Assert.Equal(AssignmentStatus.Assigned, (await SavedAssignmentAsync(waiting.Id, cancellationToken)).Status);
        Assert.Equal(AssignmentStatus.Completed, (await SavedAssignmentAsync(completed.Id, cancellationToken)).Status);
        Assert.Equal(SessionStatus.Submitted, (await SavedSessionAsync(sessionId, cancellationToken)).Status);
        Assert.Equal(0, await AuditCountAsync("assignments.assignment.expired", waiting.Id.Value, cancellationToken));
        Assert.Equal(0, await AuditCountAsync("assignments.assignment.expired", completed.Id.Value, cancellationToken));
    }

    [Fact]
    public async Task A_second_maintenance_run_finds_nothing_left_to_close()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, _, sessionId) = await StartedAsync(cancellationToken);
        await RunMaintenanceAsync(cancellationToken);

        var again = await RunMaintenanceAsync(cancellationToken);

        Assert.Equal((0, 0), (again.AssignmentsExpired, again.SessionsAbandoned));
        Assert.Empty(again.Failures);
        Assert.Equal(1, await AuditCountAsync("sessions.session.abandoned", sessionId.Value, cancellationToken));
        Assert.Equal(1, await AuditCountAsync("assignments.assignment.expired", assignment.Id.Value, cancellationToken));
    }

    [Fact]
    public async Task Listing_overdue_assignments_puts_the_longest_overdue_first_and_passes_over_the_ones_already_tried()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (earlier, _) = await AssignedAsync(cancellationToken, TestData.Now.AddHours(-3));
        var (later, _) = await AssignedAsync(cancellationToken, TestData.Now.AddHours(-2));

        await using var scope = fixture.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAssessmentAssignmentRepository>();
        var all = (await repository.ListOverdueIdsAsync(TestData.Now, 10_000, [], cancellationToken)).ToList();
        var untried = await repository.ListOverdueIdsAsync(TestData.Now, 10_000, [earlier.Id], cancellationToken);

        Assert.InRange(all.IndexOf(earlier.Id), 0, all.IndexOf(later.Id) - 1);
        Assert.DoesNotContain(earlier.Id, untried);
        Assert.Contains(later.Id, untried);
        Assert.Equal(all.Count - 1, untried.Count);
    }

    [Fact]
    public async Task Closing_one_assignment_that_is_not_overdue_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, _) = await AssignedAsync(cancellationToken, TestData.Now.AddMinutes(1));

        await using var scope = fixture.CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<ExpireOverdueAssignment>().HandleAsync(assignment.Id, cancellationToken);

        Assert.Equal(OverdueAssignmentOutcome.Unchanged, TestData.Ok(outcome));
        Assert.Equal(AssignmentStatus.Assigned, (await SavedAssignmentAsync(assignment.Id, cancellationToken)).Status);
    }
}
