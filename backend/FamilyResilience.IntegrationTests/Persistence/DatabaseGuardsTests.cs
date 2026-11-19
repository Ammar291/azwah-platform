using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class DatabaseGuardsTests(DatabaseFixture fixture)
{
    private const string RestrictViolation = "23001";

    [Fact]
    public async Task Approved_instrument_version_cannot_be_changed_or_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var versionId = (await fixture.ApprovedVersionAsync(cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE instrument_versions SET content_reference_en = 'changed' WHERE id = {versionId}", cancellationToken),
            RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM instrument_versions WHERE id = {versionId}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Approved_version_can_still_be_retired_through_the_domain_and_then_never_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);

        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(version.Id, cancellationToken);
                Assert.NotNull(tracked);
                TestData.Ok(tracked.Retire(TestData.Now, TestData.Now));
            },
            cancellationToken);
        var versionId = version.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE instrument_versions SET effective_to = effective_to + interval '1 day' WHERE id = {versionId}",
                cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Content_of_an_approved_version_cannot_be_inserted_changed_or_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var item = version.Sections[0].Items[0];
        var itemId = item.Id.Value;
        var optionId = item.Options[0].Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE instrument_items SET prompt_en = 'changed' WHERE id = {itemId}", cancellationToken),
            RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 INSERT INTO response_options (id, instrument_item_id, code, display_order, value, label_ar, label_en)
                 VALUES (gen_random_uuid(), {itemId}, 'o9', 9, 9, 'خيار', 'Option')
                 """,
                cancellationToken),
            RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM response_options WHERE id = {optionId}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Draft_content_can_change_and_a_draft_version_can_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var draft = await fixture.DraftVersionAsync(cancellationToken);
        var itemId = draft.Sections[0].Items[0].Id.Value;
        var draftId = draft.Id.Value;

        Assert.Equal(1, await fixture.ExecuteSqlAsync(
            $"UPDATE instrument_items SET prompt_en = 'Edited while draft' WHERE id = {itemId}", cancellationToken));
        Assert.Equal(1, await fixture.ExecuteSqlAsync(
            $"DELETE FROM instrument_versions WHERE id = {draftId}", cancellationToken));
    }

    [Fact]
    public async Task Approved_rule_set_cannot_be_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var ruleSetId = (await fixture.ApprovedRuleSetAsync(RuleSetKind.Eligibility, version, cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE rule_sets SET definition = '{{\"changed\": true}}'::jsonb WHERE id = {ruleSetId}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Approved_consent_policy_cannot_be_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var policyId = (await fixture.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE consent_policy_versions SET document_reference_en = 'changed' WHERE id = {policyId}",
                cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Responses_of_a_submitted_session_cannot_change()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionId = (await fixture.SubmittedSessionAsync(cancellationToken)).Session.Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE assessment_responses SET last_modified_at = last_modified_at WHERE assessment_session_id = {sessionId}",
                cancellationToken),
            RestrictViolation);
        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"DELETE FROM assessment_responses WHERE assessment_session_id = {sessionId}", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Responses_can_be_deleted_only_by_an_erasure_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionId = (await fixture.SubmittedSessionAsync(cancellationToken)).Session.Id.Value;

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var erasure = new NpgsqlCommand("SET LOCAL azwah.erasure = 'on'", connection, transaction))
        {
            await erasure.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var delete = new NpgsqlCommand(
            "DELETE FROM assessment_responses WHERE assessment_session_id = @session", connection, transaction);
        delete.Parameters.AddWithValue("session", sessionId);

        Assert.Equal(4, await delete.ExecuteNonQueryAsync(cancellationToken));
        await transaction.RollbackAsync(cancellationToken);
    }

    [Fact]
    public async Task Audit_events_cannot_be_updated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = Guid.NewGuid().ToString();
        await fixture.InScopeAsync(
            services =>
            {
                TestData.Ok(services.GetRequiredService<IAuditLog>().Stage(
                    new AuditRecord("system.guard.tested", "integration_test", resourceId, AuditOutcome.Succeeded)));
                return Task.CompletedTask;
            },
            cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE audit_events SET outcome = 'Failed' WHERE resource_id = {resourceId}", cancellationToken),
            RestrictViolation);
    }

    private async Task<string> AuditedResourceAsync(CancellationToken cancellationToken)
    {
        var resourceId = Guid.NewGuid().ToString();
        await fixture.InScopeAsync(
            services =>
            {
                TestData.Ok(services.GetRequiredService<IAuditLog>().Stage(
                    new AuditRecord("system.guard.tested", "integration_test", resourceId, AuditOutcome.Succeeded)));
                return Task.CompletedTask;
            },
            cancellationToken);
        return resourceId;
    }

    /// <summary>Runs <paramref name="sql"/> in a transaction that is always rolled back, after <paramref name="setup"/>.</summary>
    private async Task<int> InRolledBackTransactionAsync(string sql, string resourceId, string? setup, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (setup is not null)
            {
                await using var prepare = new NpgsqlCommand(setup, connection, transaction);
                await prepare.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("resource", resourceId);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task Audit_events_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = await AuditedResourceAsync(cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => InRolledBackTransactionAsync("DELETE FROM audit_events WHERE resource_id = @resource", resourceId, null, cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Audit_events_cannot_be_truncated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = await AuditedResourceAsync(cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => InRolledBackTransactionAsync("TRUNCATE audit_events", resourceId, null, cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Audit_events_can_be_deleted_only_in_a_transaction_that_sets_audit_retention()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = await AuditedResourceAsync(cancellationToken);

        var deleted = await InRolledBackTransactionAsync(
            "DELETE FROM audit_events WHERE resource_id = @resource", resourceId, "SET LOCAL azwah.audit_retention = 'on'", cancellationToken);

        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task The_erasure_setting_does_not_open_audit_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var resourceId = await AuditedResourceAsync(cancellationToken);

        await PostgresAssert.RejectsAsync(
            () => InRolledBackTransactionAsync(
                "DELETE FROM audit_events WHERE resource_id = @resource", resourceId, "SET LOCAL azwah.erasure = 'on'", cancellationToken),
            RestrictViolation);
    }

    [Fact]
    public async Task Overlapping_approved_versions_return_effective_window_overlaps()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.ApprovedVersionAsync(cancellationToken);
        var instrument = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentRepository>().GetAsync(first.InstrumentId, cancellationToken),
            cancellationToken);
        Assert.NotNull(instrument);
        var second = await fixture.DraftVersionAsync(cancellationToken, instrument, versionNumber: 2);
        var owner = await fixture.InstrumentOfAsync(first, cancellationToken);

        await using var scope = fixture.CreateScope();
        var tracked = await scope.ServiceProvider.GetRequiredService<IInstrumentVersionRepository>().GetAsync(second.Id, cancellationToken);
        Assert.NotNull(tracked);
        TestData.Ok(tracked.Approve(owner, TestData.TestApproval(), TestData.Now, null, TestData.Now));
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(QuestionnaireErrors.EffectiveWindowOverlaps, result.Error);
    }

    [Fact]
    public async Task Retiring_then_approving_in_one_transaction_succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.ApprovedVersionAsync(cancellationToken);
        var instrument = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentRepository>().GetAsync(first.InstrumentId, cancellationToken),
            cancellationToken);
        Assert.NotNull(instrument);
        var second = await fixture.DraftVersionAsync(cancellationToken, instrument, versionNumber: 2);
        var owner = await fixture.InstrumentOfAsync(first, cancellationToken);
        var switchover = TestData.Now;

        await using (var scope = fixture.CreateScope())
        {
            var versions = scope.ServiceProvider.GetRequiredService<IInstrumentVersionRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var result = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var old = await versions.GetAsync(first.Id, token);
                    var replacement = await versions.GetAsync(second.Id, token);
                    if (old is null || replacement is null)
                    {
                        return Result.Failure<bool>(new Error("tests.missing", "A version was not saved."));
                    }

                    TestData.Ok(old.Retire(switchover, switchover));
                    var retired = await unitOfWork.CommitAsync(token);
                    if (retired.IsFailure)
                    {
                        return Result.Failure<bool>(retired.Error!);
                    }

                    TestData.Ok(replacement.Approve(owner, TestData.TestApproval(), switchover, null, switchover));
                    var approved = await unitOfWork.CommitAsync(token);
                    return approved.IsSuccess ? Result.Success(true) : Result.Failure<bool>(approved.Error!);
                },
                cancellationToken);

            Assert.True(result.IsSuccess);
        }

        var effective = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentVersionRepository>()
                .GetEffectiveApprovedAsync(first.InstrumentId, switchover.AddHours(1), cancellationToken),
            cancellationToken);
        Assert.Equal(second.Id, effective?.Id);
    }

    [Fact]
    public async Task A_scheduled_end_hands_over_to_the_next_version_without_a_gap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.ApprovedVersionAsync(cancellationToken);
        var instrument = await fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentRepository>().GetAsync(first.InstrumentId, cancellationToken),
            cancellationToken);
        Assert.NotNull(instrument);
        var second = await fixture.DraftVersionAsync(cancellationToken, instrument, versionNumber: 2);
        var owner = await fixture.InstrumentOfAsync(first, cancellationToken);
        var switchover = TestData.Now.AddDays(30);

        await fixture.InScopeAsync(
            async services =>
            {
                var old = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(first.Id, cancellationToken);
                Assert.NotNull(old);
                TestData.Ok(old.ScheduleEnd(switchover, TestData.Now));
            },
            cancellationToken);

        await using (var scope = fixture.CreateScope())
        {
            var replacement = await scope.ServiceProvider.GetRequiredService<IInstrumentVersionRepository>().GetAsync(second.Id, cancellationToken);
            Assert.NotNull(replacement);
            TestData.Ok(replacement.Approve(owner, TestData.TestApproval(), switchover, null, TestData.Now));
            var approved = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            Assert.True(approved.IsSuccess, approved.Error?.Code);
        }

        Assert.Equal(first.Id, (await EffectiveAtAsync(first.InstrumentId, switchover.AddSeconds(-1), cancellationToken))?.Id);
        Assert.Equal(second.Id, (await EffectiveAtAsync(first.InstrumentId, switchover, cancellationToken))?.Id);
        Assert.Equal(second.Id, (await EffectiveAtAsync(first.InstrumentId, switchover.AddDays(1), cancellationToken))?.Id);
    }

    [Fact]
    public async Task A_member_with_assessment_history_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberId = (await fixture.StartedSessionAsync(cancellationToken)).Member.Id.Value;

        var exception = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM family_members WHERE id = {memberId}", cancellationToken),
            RestrictViolation);
        Assert.StartsWith("fk_", exception.ConstraintName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_with_responses_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionId = (await fixture.SubmittedSessionAsync(cancellationToken)).Session.Id.Value;

        var exception = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"DELETE FROM assessment_sessions WHERE id = {sessionId}", cancellationToken),
            RestrictViolation);
        Assert.StartsWith("fk_", exception.ConstraintName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_response_cannot_be_moved_from_an_open_session_into_a_submitted_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (submitted, version, _, _) = await fixture.StartedSessionAsync(cancellationToken);
        var items = version.GetItems().ToList();

        // The target session answers only the first item, then is marked submitted directly (the guard under test
        // is on the responses table, which the session row does not use).
        await AnswerAsync(submitted.Id, version, items[0], cancellationToken);
        var submittedId = submitted.Id.Value;
        Assert.Equal(1, await fixture.ExecuteSqlAsync(
            $"UPDATE assessment_sessions SET status = 'Submitted', submitted_at = started_at WHERE id = {submittedId}",
            cancellationToken));

        // A second member answers the second item in an open session of the same version, so the composite foreign
        // keys and the unique (session, item) index would all accept the move.
        var otherMember = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var otherAssignment = await fixture.AssignmentAsync(otherMember, version, cancellationToken);
        var open = await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(otherAssignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, TestData.Now));
                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started;
            },
            cancellationToken);
        await AnswerAsync(open.Id, version, items[1], cancellationToken);
        var openId = open.Id.Value;

        var exception = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE assessment_responses SET assessment_session_id = {submittedId} WHERE assessment_session_id = {openId}",
                cancellationToken),
            RestrictViolation);
        Assert.Null(exception.ConstraintName);
    }

    [Fact]
    public async Task Content_cannot_be_moved_from_a_draft_into_an_approved_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var approved = await fixture.ApprovedVersionAsync(cancellationToken);
        var approvedSectionId = approved.Sections[0].Id.Value;
        var approvedVersionId = approved.Id.Value;
        var approvedItemId = approved.Sections[0].Items[0].Id.Value;
        var draft = await fixture.DraftVersionAsync(cancellationToken);
        var draftItem = draft.Sections[0].Items[0];
        var draftItemId = draftItem.Id.Value;
        var draftOptionId = draftItem.Options[0].Id.Value;

        // Code, order and value are chosen so no unique index or composite foreign key would refuse the move.
        var option = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 UPDATE response_options
                 SET instrument_item_id = {approvedItemId}, code = 'moved', display_order = 9, value = 9
                 WHERE id = {draftOptionId}
                 """,
                cancellationToken),
            RestrictViolation);
        Assert.Null(option.ConstraintName);

        var item = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 UPDATE instrument_items
                 SET instrument_version_id = {approvedVersionId}, instrument_section_id = {approvedSectionId},
                     code = 'MOVED', display_order = 9
                 WHERE id = {draftItemId}
                 """,
                cancellationToken),
            RestrictViolation);
        Assert.Null(item.ConstraintName);
    }

    [Fact]
    public async Task A_submitted_session_cannot_be_reopened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessionId = (await fixture.SubmittedSessionAsync(cancellationToken)).Session.Id.Value;

        // The reopened row would satisfy the status-shape check, so only the session trigger can refuse it.
        var exception = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE assessment_sessions SET status = 'InProgress', submitted_at = NULL WHERE id = {sessionId}",
                cancellationToken),
            RestrictViolation);
        Assert.Null(exception.ConstraintName);
    }

    [Fact]
    public async Task A_session_cannot_change_its_section()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, version) = await ModuleAssignmentAsync(cancellationToken);
        var session = await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, TestData.Now));
                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started;
            },
            cancellationToken);
        var sessionId = session.Id.Value;
        var otherSectionId = OtherSection(version, assignment).Value;

        // The other section belongs to the same version, so the (section, version) foreign key accepts it.
        var exception = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE assessment_sessions SET instrument_section_id = {otherSectionId} WHERE id = {sessionId}",
                cancellationToken),
            RestrictViolation);
        Assert.Null(exception.ConstraintName);
    }

    [Fact]
    public async Task A_session_must_copy_its_assignments_section()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, version) = await ModuleAssignmentAsync(cancellationToken);
        var assignmentId = assignment.Id.Value;
        var memberId = assignment.FamilyMemberId.Value;
        var versionId = version.Id.Value;
        var otherSectionId = OtherSection(version, assignment).Value;
        var startedAt = TestData.Now;

        // Assignment, member, version and section all satisfy the composite foreign keys, and the assignment has no
        // live session yet, so only the session trigger can refuse the row.
        var exception = await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"""
                 INSERT INTO assessment_sessions
                     (id, assessment_assignment_id, family_member_id, instrument_version_id, instrument_section_id,
                      status, started_at, updated_at)
                 VALUES (gen_random_uuid(), {assignmentId}, {memberId}, {versionId}, {otherSectionId},
                         'InProgress', {startedAt}, {startedAt})
                 """,
                cancellationToken),
            RestrictViolation);
        Assert.Null(exception.ConstraintName);
    }

    [Fact]
    public async Task Normal_module_session_flows_pass_the_session_trigger()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (assignment, version) = await ModuleAssignmentAsync(cancellationToken);
        var section = version.Sections.Single(candidate => candidate.Id == assignment.InstrumentSectionId);

        // Start (INSERT with the section copy), then abandon: InProgress → Abandoned, assignment back to Assigned.
        var abandoned = await StartModuleSessionAsync(assignment.Id, cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(abandoned, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                TestData.Ok(session.Abandon(tracked, TestData.Now));
            },
            cancellationToken);

        // A new session, answered (InProgress → InProgress) and submitted (InProgress → Submitted) in separate commits.
        var resumed = await StartModuleSessionAsync(assignment.Id, cancellationToken);
        foreach (var item in section.Items)
        {
            await AnswerAsync(resumed, version, item, cancellationToken);
        }

        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(resumed, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                TestData.Ok(session.Submit(version, tracked, TestData.Now));
            },
            cancellationToken);

        var statuses = await fixture.QueryAsync(db => db.Set<AssessmentSession>()
            .Where(session => session.AssessmentAssignmentId == assignment.Id)
            .ToDictionaryAsync(session => session.Id, session => session.Status, cancellationToken));
        Assert.Equal(SessionStatus.Abandoned, statuses[abandoned]);
        Assert.Equal(SessionStatus.Submitted, statuses[resumed]);
    }

    private async Task<AssessmentSessionId> StartModuleSessionAsync(
        AssessmentAssignmentId assignmentId, CancellationToken cancellationToken) =>
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignmentId, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, TestData.Now));
                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started.Id;
            },
            cancellationToken);

    private Task<InstrumentVersion?> EffectiveAtAsync(InstrumentId instrumentId, DateTimeOffset at, CancellationToken cancellationToken) =>
        fixture.InScopeAsync(
            services => services.GetRequiredService<IInstrumentVersionRepository>().GetEffectiveApprovedAsync(instrumentId, at, cancellationToken),
            cancellationToken);

    /// <summary>A saved assignment of one section (module) of a new approved version, with no session yet.</summary>
    private async Task<(AssessmentAssignment Assignment, InstrumentVersion Version)> ModuleAssignmentAsync(
        CancellationToken cancellationToken)
    {
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var section = version.Sections.Single(candidate => candidate.Code == "A");
        var assignment = await fixture.AssignmentAsync(member, version, cancellationToken, section.Id);
        return (assignment, version);
    }

    private static InstrumentSectionId OtherSection(InstrumentVersion version, AssessmentAssignment assignment) =>
        version.Sections.Single(section => section.Id != assignment.InstrumentSectionId).Id;

    private async Task AnswerAsync(
        AssessmentSessionId sessionId, InstrumentVersion version, InstrumentItem item, CancellationToken cancellationToken) =>
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                TestData.Ok(tracked.Answer(version, item.Id, item.Options.OrderBy(option => option.Value).First().Id, TestData.Now));
            },
            cancellationToken);
}
