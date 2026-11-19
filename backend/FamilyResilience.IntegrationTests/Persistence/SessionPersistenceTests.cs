using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class SessionPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Submitted_session_with_its_responses_round_trips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, assignment, member) = await fixture.SubmittedSessionAsync(cancellationToken);

        var (loaded, loadedAssignment) = await fixture.InScopeAsync(
            async services => (
                await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken),
                await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)),
            cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(SessionStatus.Submitted, loaded.Status);
        Assert.Equal(member.Id, loaded.FamilyMemberId);
        Assert.Equal(version.Id, loaded.InstrumentVersionId);
        Assert.Equal(TestData.Now, loaded.SubmittedAt);
        Assert.Equal(4, loaded.Responses.Count);
        Assert.All(loaded.Responses, response => Assert.Equal(1, response.RawValue));
        Assert.Equal(AssignmentStatus.Completed, loadedAssignment?.Status);
    }

    [Fact]
    public async Task Submitting_writes_the_session_submitted_outbox_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await fixture.SubmittedSessionAsync(cancellationToken);

        var messages = await fixture.OutboxMessagesAsync(
            "sessions.assessment_session_submitted.v1", "assessmentSessionId", session.Id.Value, cancellationToken);

        Assert.Single(messages);
    }

    [Fact]
    public async Task Changing_an_answer_updates_the_single_response_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.StartedSessionAsync(cancellationToken);
        var item = version.Sections[0].Items[0];

        foreach (var (option, minutes) in new[] { (item.Options.Single(o => o.Value == 1), 1), (item.Options.Single(o => o.Value == 3), 2) })
        {
            await fixture.InScopeAsync(
                async services =>
                {
                    var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken);
                    Assert.NotNull(tracked);
                    TestData.Ok(tracked.Answer(version, item.Id, option.Id, TestData.Now.AddMinutes(minutes)));
                },
                cancellationToken);
        }

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken),
            cancellationToken);
        Assert.NotNull(loaded);
        var response = Assert.Single(loaded.Responses);
        Assert.Equal(3, response.RawValue);
        Assert.Equal(TestData.Now.AddMinutes(1), response.AnsweredAt);
        Assert.Equal(TestData.Now.AddMinutes(2), response.LastModifiedAt);
    }

    [Fact]
    public async Task Concurrent_answers_to_the_same_session_return_a_concurrency_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.StartedSessionAsync(cancellationToken);
        await using var first = fixture.CreateScope();
        await using var second = fixture.CreateScope();
        var firstCopy = await first.ServiceProvider.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken);
        var secondCopy = await second.ServiceProvider.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken);
        Assert.NotNull(firstCopy);
        Assert.NotNull(secondCopy);
        var itemA = version.Sections[0].Items[0];
        var itemB = version.Sections[1].Items[0];

        TestData.Ok(firstCopy.Answer(version, itemA.Id, itemA.Options.OrderBy(o => o.Value).First().Id, TestData.Now.AddMinutes(1)));
        Assert.True((await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken)).IsSuccess);
        TestData.Ok(secondCopy.Answer(version, itemB.Id, itemB.Options.OrderBy(o => o.Value).First().Id, TestData.Now.AddMinutes(1)));
        var result = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(PersistenceErrors.ConcurrencyConflict, result.Error);
    }

    [Fact]
    public async Task The_live_session_of_an_assignment_is_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, assignment, _) = await fixture.StartedSessionAsync(cancellationToken);

        var live = await fixture.InScopeAsync(
            services => services.GetRequiredService<IAssessmentSessionRepository>().GetLiveForAssignmentAsync(assignment.Id, cancellationToken),
            cancellationToken);

        Assert.Equal(session.Id, live?.Id);
    }

    [Fact]
    public async Task Database_allows_one_live_session_per_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, _, _, _) = await fixture.StartedSessionAsync(cancellationToken);
        var sessionId = session.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 INSERT INTO assessment_sessions
                     (id, assessment_assignment_id, family_member_id, instrument_version_id, instrument_section_id,
                      status, started_at, updated_at)
                 SELECT gen_random_uuid(), assessment_assignment_id, family_member_id, instrument_version_id,
                        instrument_section_id, 'InProgress', started_at, updated_at
                 FROM assessment_sessions WHERE id = {sessionId}
                 """,
                cancellationToken),
            PostgresErrorCodes.UniqueViolation,
            "ux_assessment_sessions_live");
    }

    [Fact]
    public async Task Database_rejects_a_session_whose_member_differs_from_its_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var assignment = await fixture.AssignmentAsync(member, version, cancellationToken);
        var otherMemberId = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0].Id.Value;
        var assignmentId = assignment.Id.Value;
        var versionId = version.Id.Value;
        var startedAt = TestData.Now;

        // An INSERT, because the session trigger refuses any UPDATE of the copied keys before foreign keys are checked.
        // The section copy (none) matches the assignment and it has no live session, so only the foreign key refuses.
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 INSERT INTO assessment_sessions
                     (id, assessment_assignment_id, family_member_id, instrument_version_id, instrument_section_id,
                      status, started_at, updated_at)
                 VALUES (gen_random_uuid(), {assignmentId}, {otherMemberId}, {versionId}, NULL,
                         'InProgress', {startedAt}, {startedAt})
                 """,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_assessment_sessions_assessment_assignments_assessment_assig");
    }

    [Fact]
    public async Task Database_rejects_a_response_for_an_item_of_another_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.StartedSessionAsync(cancellationToken);
        // The item and option both belong to another version, so only the (item, version) key can refuse the row.
        var otherItem = (await fixture.ApprovedVersionAsync(cancellationToken)).Sections[0].Items[0];
        var otherOption = otherItem.Options.OrderBy(o => o.Value).First();
        var sessionId = session.Id.Value;
        var versionId = version.Id.Value;
        var otherItemId = otherItem.Id.Value;
        var otherOptionId = otherOption.Id.Value;
        var otherRawValue = otherOption.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 INSERT INTO assessment_responses
                     (id, assessment_session_id, instrument_version_id, instrument_item_id, response_option_id, raw_value,
                      answered_at, last_modified_at)
                 VALUES (gen_random_uuid(), {sessionId}, {versionId}, {otherItemId}, {otherOptionId}, {otherRawValue}, now(), now())
                 """,
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_assessment_responses_instrument_item_instrument_item_id_ins");
    }

    [Fact]
    public async Task Database_rejects_a_raw_value_that_differs_from_its_option()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (session, version, _, _) = await fixture.StartedSessionAsync(cancellationToken);
        var item = version.Sections[0].Items[0];
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session.Id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(tracked.Answer(version, item.Id, item.Options.OrderBy(option => option.Value).First().Id, TestData.Now));
            },
            cancellationToken);
        var sessionId = session.Id.Value;
        var itemId = item.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE assessment_responses SET raw_value = 3 WHERE assessment_session_id = {sessionId} AND instrument_item_id = {itemId}",
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_assessment_responses_response_option_response_option_id_ins");
    }
}
