using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>
/// Each flow stages its audit event with <see cref="IAuditLog.Stage"/>, so the business rows, the audit event and the
/// outbox message are written by one commit or not at all.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TransactionFlowTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Household_roster_audit_and_outbox_commit_together()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = TestData.NewHousehold(TestData.Head(), TestData.Spouse(), TestData.Child(4));

        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IHouseholdRepository>().Add(household);
            TestData.Ok(scope.ServiceProvider.GetRequiredService<IAuditLog>().Stage(
                new AuditRecord("households.household.created", "household", household.Id.Value.ToString(), AuditOutcome.Succeeded)));
            Assert.True((await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken)).IsSuccess);
        }

        Assert.Equal(3, await fixture.QueryAsync(db =>
            db.Set<FamilyResilience.Domain.Members.FamilyMember>().CountAsync(member => member.HouseholdId == household.Id, cancellationToken)));
        Assert.Equal(1, await AuditCountAsync(household.Id.Value, cancellationToken));
        Assert.Single(await fixture.OutboxMessagesAsync(
            "households.household_created.v1", "householdId", household.Id.Value, cancellationToken));
    }

    [Fact]
    public async Task A_failed_household_commit_leaves_no_audit_event_or_outbox_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existing = await fixture.SavedHouseholdAsync(cancellationToken);
        var duplicate = TestData.Ok(Household.Create(existing.Reference, [TestData.Head()], TestData.Now));

        await using (var scope = fixture.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IHouseholdRepository>().Add(duplicate);
            TestData.Ok(scope.ServiceProvider.GetRequiredService<IAuditLog>().Stage(
                new AuditRecord("households.household.created", "household", duplicate.Id.Value.ToString(), AuditOutcome.Succeeded)));
            var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
            Assert.Equal(HouseholdErrors.ReferenceTaken, result.Error);
        }

        Assert.Equal(0, await AuditCountAsync(duplicate.Id.Value, cancellationToken));
        Assert.Empty(await fixture.OutboxMessagesAsync(
            "households.household_created.v1", "householdId", duplicate.Id.Value, cancellationToken));
    }

    [Fact]
    public async Task Assignments_for_a_wave_commit_together_or_not_at_all()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head(), TestData.Spouse());
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var wave = await fixture.OpenWaveAsync(cancellationToken);
        var rules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Eligibility, version, cancellationToken);
        var sectionA = version.Sections.Single(section => section.Code == "A").Id;
        var sectionB = version.Sections.Single(section => section.Code == "B").Id;

        AssessmentAssignment Assign(int memberIndex, InstrumentSectionId section) =>
            TestData.Ok(AssessmentAssignment.Assign(
                household.Members[memberIndex], version, section, wave, rules, AssignmentReason.EligibilityRule, null, TestData.Now));

        // The second batch repeats the head's module A assignment, so its whole commit must fail.
        var committed = new[] { Assign(0, sectionA), Assign(1, sectionA) };
        var rejected = new[] { Assign(0, sectionB), Assign(0, sectionA) };

        Assert.True((await CommitAssignmentsAsync(committed, cancellationToken)).IsSuccess);
        var failure = await CommitAssignmentsAsync(rejected, cancellationToken);

        Assert.Equal(AssignmentErrors.AlreadyAssigned, failure.Error);
        foreach (var assignment in committed)
        {
            Assert.Equal(1, await AuditCountAsync(assignment.Id.Value, cancellationToken));
            Assert.Single(await fixture.OutboxMessagesAsync(
                "assignments.assessment_assigned.v1", "assessmentAssignmentId", assignment.Id.Value, cancellationToken));
        }

        foreach (var assignment in rejected)
        {
            Assert.Null(await fixture.QueryAsync(db =>
                db.AssessmentAssignments.SingleOrDefaultAsync(saved => saved.Id == assignment.Id, cancellationToken)));
            Assert.Equal(0, await AuditCountAsync(assignment.Id.Value, cancellationToken));
            Assert.Empty(await fixture.OutboxMessagesAsync(
                "assignments.assessment_assigned.v1", "assessmentAssignmentId", assignment.Id.Value, cancellationToken));
        }
    }

    [Fact]
    public async Task Answers_then_submission_commit_session_assignment_audit_and_outbox()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (started, version, assignment, _) = await fixture.StartedSessionAsync(cancellationToken);

        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, cancellationToken);
                Assert.NotNull(session);
                foreach (var item in version.GetItems())
                {
                    TestData.Ok(session.Answer(version, item.Id, item.Options[1].Id, TestData.Now));
                }
            },
            cancellationToken);

        await using (var scope = fixture.CreateScope())
        {
            var services = scope.ServiceProvider;
            var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, cancellationToken);
            var trackedAssignment = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken);
            Assert.NotNull(session);
            Assert.NotNull(trackedAssignment);
            TestData.Ok(session.Submit(version, trackedAssignment, TestData.Now.AddMinutes(5)));
            TestData.Ok(services.GetRequiredService<IAuditLog>().Stage(
                new AuditRecord("sessions.session.submitted", "assessment_session", started.Id.Value.ToString(), AuditOutcome.Succeeded)));
            Assert.True((await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken)).IsSuccess);
        }

        var (saved, savedAssignment) = await fixture.InScopeAsync(
            async services => (
                await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, cancellationToken),
                await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)),
            cancellationToken);
        Assert.Equal(SessionStatus.Submitted, saved?.Status);
        Assert.Equal(4, saved?.Responses.Count);
        Assert.Equal(AssignmentStatus.Completed, savedAssignment?.Status);
        Assert.Equal(1, await AuditCountAsync(started.Id.Value, cancellationToken));
        Assert.Single(await fixture.OutboxMessagesAsync(
            "sessions.assessment_session_submitted.v1", "assessmentSessionId", started.Id.Value, cancellationToken));
    }

    [Fact]
    public async Task A_failed_explicit_transaction_rolls_back_the_submission_its_audit_event_and_outbox_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (started, version, assignment, _) = await fixture.StartedSessionAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, cancellationToken);
                Assert.NotNull(session);
                foreach (var item in version.GetItems())
                {
                    TestData.Ok(session.Answer(version, item.Id, item.Options[0].Id, TestData.Now));
                }
            },
            cancellationToken);
        var abort = new Error("tests.abort", "A later step of the use case failed.");

        await using (var scope = fixture.CreateScope())
        {
            var services = scope.ServiceProvider;
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();

            var result = await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, token);
                    var trackedAssignment = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, token);
                    if (session is null || trackedAssignment is null)
                    {
                        return Result.Failure<bool>(new Error("tests.missing", "The session was not saved."));
                    }

                    TestData.Ok(session.Submit(version, trackedAssignment, TestData.Now.AddMinutes(5)));
                    services.GetRequiredService<IAuditLog>().Stage(
                        new AuditRecord("sessions.session.submitted", "assessment_session", started.Id.Value.ToString(), AuditOutcome.Succeeded));
                    Assert.True((await unitOfWork.CommitAsync(token)).IsSuccess);
                    return Result.Failure<bool>(abort);
                },
                cancellationToken);

            Assert.Equal(abort, result.Error);
        }

        var (saved, savedAssignment) = await fixture.InScopeAsync(
            async services => (
                await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(started.Id, cancellationToken),
                await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)),
            cancellationToken);
        Assert.Equal(SessionStatus.InProgress, saved?.Status);
        Assert.Equal(AssignmentStatus.InProgress, savedAssignment?.Status);
        Assert.Equal(0, await AuditCountAsync(started.Id.Value, cancellationToken));
        Assert.Empty(await fixture.OutboxMessagesAsync(
            "sessions.assessment_session_submitted.v1", "assessmentSessionId", started.Id.Value, cancellationToken));
    }

    [Fact]
    public async Task Score_session_status_referral_audit_and_outbox_commit_in_one_transaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = await ScoringAndReferralSetupAsync(cancellationToken);

        Result<SupportReferral> result;
        await using (var scope = fixture.CreateScope())
        {
            result = await ScoreAndReferAsync(scope.ServiceProvider, setup, abort: null, cancellationToken);
        }

        Assert.True(result.IsSuccess);
        var referralId = result.Value.Id;
        var savedSession = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(s => s.Id == setup.Session.Id, cancellationToken));
        var savedReferral = await fixture.QueryAsync(db => db.SupportReferrals.SingleOrDefaultAsync(r => r.Id == referralId, cancellationToken));
        Assert.Equal(SessionStatus.Scored, savedSession.Status);
        Assert.Equal(1, await fixture.QueryAsync(db => db.ScoreResults.CountAsync(r => r.AssessmentSessionId == setup.Session.Id, cancellationToken)));
        Assert.Equal(setup.Consent.Id, savedReferral?.ConsentRecordId);
        Assert.Equal(1, await AuditCountAsync(referralId.Value, cancellationToken));
        Assert.Single(await fixture.OutboxMessagesAsync(
            "scoring.score_result_recorded.v1", "assessmentSessionId", setup.Session.Id.Value, cancellationToken));
        Assert.Single(await fixture.OutboxMessagesAsync(
            "referrals.support_referral_created.v1", "supportReferralId", referralId.Value, cancellationToken));
    }

    [Fact]
    public async Task A_failed_scoring_transaction_leaves_no_score_referral_audit_event_or_outbox_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var setup = await ScoringAndReferralSetupAsync(cancellationToken);
        var abort = new Error("tests.abort", "A later step of the use case failed.");

        Result<SupportReferral> result;
        SupportReferralId? committedInside = null;
        await using (var scope = fixture.CreateScope())
        {
            result = await ScoreAndReferAsync(
                scope.ServiceProvider, setup, abort, cancellationToken, referralId => committedInside = referralId);
        }

        Assert.Equal(abort, result.Error);
        Assert.NotNull(committedInside);
        Assert.Equal(0, await AuditCountAsync(committedInside.Value.Value, cancellationToken));
        Assert.Empty(await fixture.OutboxMessagesAsync(
            "referrals.support_referral_created.v1", "supportReferralId", committedInside.Value.Value, cancellationToken));
        var savedSession = await fixture.QueryAsync(db => db.AssessmentSessions.SingleAsync(s => s.Id == setup.Session.Id, cancellationToken));
        Assert.Equal(SessionStatus.Submitted, savedSession.Status);
        Assert.Equal(0, await fixture.QueryAsync(db => db.ScoreResults.CountAsync(r => r.AssessmentSessionId == setup.Session.Id, cancellationToken)));
        Assert.Equal(0, await fixture.QueryAsync(db => db.SupportReferrals.CountAsync(r => r.AssessmentSessionId == setup.Session.Id, cancellationToken)));
        Assert.Empty(await fixture.OutboxMessagesAsync(
            "scoring.score_result_recorded.v1", "assessmentSessionId", setup.Session.Id.Value, cancellationToken));
        Assert.Empty(await fixture.OutboxMessagesAsync(
            "referrals.support_referral_created.v1", "assessmentSessionId", setup.Session.Id.Value, cancellationToken));
    }

    /// <summary>A submitted, unscored session with approved scoring and referral rule sets and the member's referral consent.</summary>
    private async Task<(AssessmentSession Session, RuleSet ScoringRules, RuleSet ReferralRules, ConsentRecord Consent)>
        ScoringAndReferralSetupAsync(CancellationToken cancellationToken)
    {
        var (session, version, _, member) = await fixture.SubmittedSessionAsync(cancellationToken);
        var scoringRules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Scoring, version, cancellationToken);
        var referralRules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Referral, version, cancellationToken);
        var consent = await fixture.GrantedConsentAsync(
            member, await fixture.ApprovedPolicyAsync(ConsentType.Referral, cancellationToken), cancellationToken);
        return (session, scoringRules, referralRules, consent);
    }

    /// <summary>
    /// The score-then-refer flow: score and commit, then read the referral consent under a share lock, refer, stage the
    /// audit event and commit again, all in one explicit transaction. Scoring commits first because the referral trigger
    /// reads the session's status, and EF Core does not promise statement order inside one save.
    /// <paramref name="onCommitted"/> receives the referral's ID once its commit succeeds, before any abort.
    /// </summary>
    private static Task<Result<SupportReferral>> ScoreAndReferAsync(
        IServiceProvider services,
        (AssessmentSession Session, RuleSet ScoringRules, RuleSet ReferralRules, ConsentRecord Consent) setup,
        Error? abort,
        CancellationToken cancellationToken,
        Action<SupportReferralId>? onCommitted = null)
    {
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        return unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(setup.Session.Id, token);
                if (session is null)
                {
                    return Result.Failure<SupportReferral>(new Error("tests.missing", "The session was not saved."));
                }

                services.GetRequiredService<IScoreResultRepository>().Add(TestData.Ok(ScoreResult.Record(
                    session, setup.ScoringRules, TestData.SyntheticComputation(session), TestData.Now.AddMinutes(1))));
                var scored = await unitOfWork.CommitAsync(token);
                if (scored.IsFailure)
                {
                    return Result.Failure<SupportReferral>(scored.Error!);
                }

                var consent = await services.GetRequiredService<IConsentRecordRepository>()
                    .GetActiveWithShareLockAsync(session.FamilyMemberId, ConsentType.Referral, token);
                if (consent is null)
                {
                    return Result.Failure<SupportReferral>(new Error("tests.missing", "The referral consent was not saved."));
                }

                var referral = TestData.Ok(SupportReferral.Create(
                    session, setup.ReferralRules, consent, "synthetic_category", TestData.Now.AddMinutes(1)));
                services.GetRequiredService<ISupportReferralRepository>().Add(referral);
                TestData.Ok(services.GetRequiredService<IAuditLog>().Stage(new AuditRecord(
                    "referrals.referral.created", "support_referral", referral.Id.Value.ToString(), AuditOutcome.Succeeded)));
                var committed = await unitOfWork.CommitAsync(token);
                if (committed.IsFailure)
                {
                    return Result.Failure<SupportReferral>(committed.Error!);
                }

                onCommitted?.Invoke(referral.Id);
                return abort is null ? Result.Success(referral) : Result.Failure<SupportReferral>(abort);
            },
            cancellationToken);
    }

    private async Task<Result> CommitAssignmentsAsync(
        IReadOnlyCollection<AssessmentAssignment> assignments, CancellationToken cancellationToken)
    {
        await using var scope = fixture.CreateScope();
        var services = scope.ServiceProvider;
        foreach (var assignment in assignments)
        {
            services.GetRequiredService<IAssessmentAssignmentRepository>().Add(assignment);
            TestData.Ok(services.GetRequiredService<IAuditLog>().Stage(new AuditRecord(
                "assignments.assessment.assigned", "assessment_assignment", assignment.Id.Value.ToString(), AuditOutcome.Succeeded)));
        }

        return await services.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
    }

    private Task<int> AuditCountAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var id = resourceId.ToString();
        return fixture.QueryAsync(db => db.AuditEvents.CountAsync(e => e.ResourceId == id, cancellationToken));
    }
}
