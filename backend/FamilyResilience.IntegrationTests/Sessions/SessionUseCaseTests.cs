using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Sessions;

[Collection(DatabaseCollection.Name)]
public sealed class SessionUseCaseTests(DatabaseFixture fixture)
{
    /// <summary>
    /// The use case with the test access rule. Production resolves CallerAccess, which finds no membership for the
    /// fixture's caller and so refuses every member.
    /// </summary>
    private async Task<T> WithAccessAsync<TUseCase, T>(Func<TUseCase, Task<T>> call)
        where TUseCase : class
    {
        await using var scope = fixture.CreateScope();
        var useCase = ActivatorUtilities.CreateInstance<TUseCase>(scope.ServiceProvider, new AllowAllParticipantAccess());
        return await call(useCase);
    }

    private async Task<(Guid SessionId, InstrumentVersion Version, AssessmentAssignment Assignment, FamilyMember Member)> StartedAsync(
        CancellationToken cancellationToken)
    {
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        await fixture.GrantedConsentAsync(member, await fixture.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken), cancellationToken);
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var assignment = await fixture.AssignmentAsync(member, version, cancellationToken);
        var started = await WithAccessAsync<StartSession, Result<SessionResponse>>(
            useCase => useCase.HandleAsync(assignment.Id.Value, cancellationToken));
        return (TestData.Ok(started).Id, version, assignment, member);
    }

    private static SaveResponsesRequest Answers(InstrumentVersion version, int optionIndex = 0) =>
        new(version.GetItems().Select(item => new AnswerRequest(item.Id.Value, item.Options[optionIndex].Id.Value)).ToList());

    private Task<Result<SubmissionResponse>> SubmitAsync(Guid sessionId, Guid key, CancellationToken cancellationToken) =>
        WithAccessAsync<SubmitSession, Result<SubmissionResponse>>(useCase => useCase.HandleAsync(sessionId, key, cancellationToken));

    private async Task AnswerEverythingAsync(Guid sessionId, InstrumentVersion version, CancellationToken cancellationToken) =>
        TestData.Ok(await WithAccessAsync<SaveResponses, Result<SessionAnswersResponse>>(
            useCase => useCase.HandleAsync(sessionId, Answers(version), cancellationToken)));

    [Fact]
    public async Task Without_an_access_rule_every_caller_is_refused_as_if_nothing_existed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var assignment = await fixture.AssignmentAsync(member, await fixture.ApprovedVersionAsync(cancellationToken), cancellationToken);

        await using var scope = fixture.CreateScope();
        var refused = await scope.ServiceProvider.GetRequiredService<StartSession>().HandleAsync(assignment.Id.Value, cancellationToken);

        Assert.Equal(AssignmentErrors.AssignmentNotFound, refused.Error);
    }

    [Fact]
    public async Task Starting_without_participation_consent_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var assignment = await fixture.AssignmentAsync(member, await fixture.ApprovedVersionAsync(cancellationToken), cancellationToken);

        var refused = await WithAccessAsync<StartSession, Result<SessionResponse>>(
            useCase => useCase.HandleAsync(assignment.Id.Value, cancellationToken));

        Assert.Equal(SessionErrors.ParticipationConsentRequired, refused.Error);
    }

    [Fact]
    public async Task Starting_answering_and_submitting_moves_the_session_through_its_lifecycle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, assignment, _) = await StartedAsync(cancellationToken);
        await AnswerEverythingAsync(sessionId, version, cancellationToken);

        var submitted = TestData.Ok(await SubmitAsync(sessionId, Guid.NewGuid(), cancellationToken));

        Assert.Equal((SubmissionOutcome.Submitted, SessionStatus.Submitted), (submitted.Outcome, submitted.Status));
        var session = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == AssessmentSessionId.From(sessionId), cancellationToken));
        var savedAssignment = await fixture.QueryAsync(db => db.AssessmentAssignments.SingleAsync(candidate => candidate.Id == assignment.Id, cancellationToken));
        Assert.Equal(SessionStatus.Submitted, session.Status);
        Assert.Equal(AssignmentStatus.Completed, savedAssignment.Status);
        foreach (var action in new[] { "sessions.session.started", "sessions.session.submitted" })
        {
            Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
                audit => audit.Action == action && audit.ResourceId == sessionId.ToString(), cancellationToken)));
        }
    }

    [Fact]
    public async Task Submitting_twice_is_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, _, _) = await StartedAsync(cancellationToken);
        await AnswerEverythingAsync(sessionId, version, cancellationToken);
        var first = TestData.Ok(await SubmitAsync(sessionId, Guid.NewGuid(), cancellationToken));

        var second = TestData.Ok(await SubmitAsync(sessionId, Guid.NewGuid(), cancellationToken));

        Assert.Equal(SubmissionOutcome.AlreadySubmitted, second.Outcome);
        Assert.Equal(first.SubmittedAt, second.SubmittedAt);
        Assert.Single(await fixture.OutboxMessagesAsync(
            "sessions.assessment_session_submitted.v1", "assessmentSessionId", sessionId, cancellationToken));
        Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "sessions.session.submitted" && audit.ResourceId == sessionId.ToString(), cancellationToken)));
    }

    [Fact]
    public async Task A_key_that_found_the_session_already_submitted_is_not_remembered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, _, _) = await StartedAsync(cancellationToken);
        await AnswerEverythingAsync(sessionId, version, cancellationToken);
        TestData.Ok(await SubmitAsync(sessionId, Guid.NewGuid(), cancellationToken));
        var key = Guid.NewGuid();

        var late = TestData.Ok(await SubmitAsync(sessionId, key, cancellationToken));
        var replayed = TestData.Ok(await SubmitAsync(sessionId, key, cancellationToken));

        Assert.Equal((SubmissionOutcome.AlreadySubmitted, SubmissionOutcome.AlreadySubmitted), (late.Outcome, replayed.Outcome));
        Assert.Equal(0, await fixture.QueryAsync(db => db.Set<IdempotencyRecord>().CountAsync(record => record.Key == key, cancellationToken)));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_returns_the_first_outcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, _, _) = await StartedAsync(cancellationToken);
        await AnswerEverythingAsync(sessionId, version, cancellationToken);
        var key = Guid.NewGuid();
        var first = TestData.Ok(await SubmitAsync(sessionId, key, cancellationToken));

        var retried = TestData.Ok(await SubmitAsync(sessionId, key, cancellationToken));

        Assert.Equal(first, retried);
        Assert.Equal(1, await fixture.QueryAsync(db => db.Set<IdempotencyRecord>().CountAsync(record => record.Key == key, cancellationToken)));
    }

    [Fact]
    public async Task A_key_used_for_one_session_is_refused_for_another()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (firstId, firstVersion, _, _) = await StartedAsync(cancellationToken);
        var (secondId, secondVersion, _, _) = await StartedAsync(cancellationToken);
        await AnswerEverythingAsync(firstId, firstVersion, cancellationToken);
        await AnswerEverythingAsync(secondId, secondVersion, cancellationToken);
        var key = Guid.NewGuid();
        TestData.Ok(await SubmitAsync(firstId, key, cancellationToken));

        var reused = await SubmitAsync(secondId, key, cancellationToken);

        Assert.Equal(RequestErrors.IdempotencyKeyReused, reused.Error);
        var untouched = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == AssessmentSessionId.From(secondId), cancellationToken));
        Assert.Equal(SessionStatus.InProgress, untouched.Status);
    }

    [Fact]
    public async Task A_failed_submission_stores_no_key_so_the_client_can_retry_with_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, _, _) = await StartedAsync(cancellationToken);
        var key = Guid.NewGuid();

        var early = await SubmitAsync(sessionId, key, cancellationToken);
        await AnswerEverythingAsync(sessionId, version, cancellationToken);
        var retried = await SubmitAsync(sessionId, key, cancellationToken);

        Assert.Equal(SessionErrors.RequiredItemsUnanswered, early.Error);
        Assert.Equal(SubmissionOutcome.Submitted, TestData.Ok(retried).Outcome);
    }

    [Fact]
    public async Task A_batch_with_one_foreign_item_saves_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, _, _) = await StartedAsync(cancellationToken);
        var foreign = (await fixture.ApprovedVersionAsync(cancellationToken)).GetItems().First();
        var answers = Answers(version).Responses!.Append(new AnswerRequest(foreign.Id.Value, foreign.Options[0].Id.Value)).ToList();

        var saved = await WithAccessAsync<SaveResponses, Result<SessionAnswersResponse>>(
            useCase => useCase.HandleAsync(sessionId, new SaveResponsesRequest(answers), cancellationToken));

        Assert.Equal(SessionErrors.ItemNotInVersion, saved.Error);
        Assert.Equal(0, await fixture.QueryAsync(db => db.AssessmentSessions
            .Where(session => session.Id == AssessmentSessionId.From(sessionId))
            .SelectMany(session => session.Responses)
            .CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Answering_after_submission_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, _, _) = await StartedAsync(cancellationToken);
        await AnswerEverythingAsync(sessionId, version, cancellationToken);
        TestData.Ok(await SubmitAsync(sessionId, Guid.NewGuid(), cancellationToken));

        var answered = await WithAccessAsync<SaveResponses, Result<SessionAnswersResponse>>(
            useCase => useCase.HandleAsync(sessionId, Answers(version, 1), cancellationToken));

        Assert.Equal(SessionErrors.NotInProgress, answered.Error);
    }

    [Fact]
    public async Task Abandoning_keeps_the_session_as_history_and_lets_a_new_one_start()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, _, assignment, _) = await StartedAsync(cancellationToken);

        TestData.Ok(await WithAccessAsync<AbandonSession, Result<SessionResponse>>(
            useCase => useCase.HandleAsync(sessionId, cancellationToken)));
        var restarted = TestData.Ok(await WithAccessAsync<StartSession, Result<SessionResponse>>(
            useCase => useCase.HandleAsync(assignment.Id.Value, cancellationToken)));

        Assert.NotEqual(sessionId, restarted.Id);
        var abandoned = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(candidate => candidate.Id == AssessmentSessionId.From(sessionId), cancellationToken));
        Assert.Equal(SessionStatus.Abandoned, abandoned.Status);
    }

    [Fact]
    public async Task A_second_live_session_on_the_same_assignment_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (_, _, assignment, _) = await StartedAsync(cancellationToken);

        var second = await WithAccessAsync<StartSession, Result<SessionResponse>>(
            useCase => useCase.HandleAsync(assignment.Id.Value, cancellationToken));

        Assert.Equal(SessionErrors.SessionAlreadyExists, second.Error);
    }

    [Theory]
    [InlineData("answer")]
    [InlineData("submit")]
    [InlineData("abandon")]
    public async Task Production_access_refuses_an_existing_session_as_if_it_did_not_exist(string useCase)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (sessionId, version, _, _) = await StartedAsync(cancellationToken);

        // Resolved from the container, so the production access rule applies: the caller has no membership.
        await using var scope = fixture.CreateScope();
        var error = useCase switch
        {
            "answer" => (await scope.ServiceProvider.GetRequiredService<SaveResponses>().HandleAsync(
                sessionId, Answers(version), cancellationToken)).Error,
            "submit" => (await scope.ServiceProvider.GetRequiredService<SubmitSession>().HandleAsync(
                sessionId, Guid.NewGuid(), cancellationToken)).Error,
            "abandon" => (await scope.ServiceProvider.GetRequiredService<AbandonSession>().HandleAsync(sessionId, cancellationToken)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(useCase), useCase, "Unknown use case."),
        };

        Assert.Equal(SessionErrors.SessionNotFound.Code, error?.Code);
        await using var check = fixture.CreateScope();
        var unchanged = await check.ServiceProvider.GetRequiredService<IAssessmentSessionRepository>()
            .GetAsync(AssessmentSessionId.From(sessionId), cancellationToken);
        Assert.NotNull(unchanged);
        Assert.Equal(SessionStatus.InProgress, unchanged.Status);
        Assert.Empty(unchanged.Responses);
    }

    [Fact]
    public async Task An_unknown_session_returns_session_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var result = await SubmitAsync(Guid.NewGuid(), Guid.NewGuid(), cancellationToken);

        Assert.Equal(SessionErrors.SessionNotFound, result.Error);
    }
}
