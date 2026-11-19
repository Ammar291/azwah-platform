using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Assignments;

[Collection(EngineDatabaseCollection.Name)]
public sealed class AssignmentRunTests(DatabaseFixture fixture)
{
    // Proposed-rule shapes, used here only as synthetic test rules for sections A and B of a synthetic version.
    private const string HeadOrEarner = """{"any":[{"isHeadOfHousehold":true},{"isPrimaryIncomeEarner":true}]}""";
    private const string WorkingParent =
        """{"all":[{"ageAtLeast":25},{"ageAtMost":55},{"employmentStatusIn":["employed"]},{"householdHasMemberYoungerThan":18}]}""";

    private static FamilyMemberDetails Spouse(EmploymentStatus? employment = EmploymentStatus.Employed) =>
        new(TestData.BornYearsAgo(40), HouseholdRelationship.Spouse, employment, false, false);

    private async Task<(Household Household, Wave Wave, Instrument Instrument)> ArrangeAsync(
        CancellationToken cancellationToken, EmploymentStatus? spouseEmployment = EmploymentStatus.Employed)
    {
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head(62), Spouse(spouseEmployment), TestData.Child(10));
        var wave = await fixture.OpenWaveAsync(cancellationToken);
        var (instrument, _, _) = await fixture.EligibilityScenarioAsync(cancellationToken, ("A", HeadOrEarner), ("B", WorkingParent));
        return (household, wave, instrument);
    }

    private async Task<Result<AssignmentRunResponse>> RunAsync(Household household, Wave wave, CancellationToken cancellationToken)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AssignHouseholdForWave>()
            .HandleAsync(household.Id.Value, new HouseholdWaveRequest(wave.Id.Value), cancellationToken);
    }

    private static List<AssignmentSummaryResponse> Only(IEnumerable<AssignmentSummaryResponse> summaries, Instrument instrument) =>
        summaries.Where(summary => summary.InstrumentCode == instrument.Code).ToList();

    private async Task ChangeSpouseAsync(Household household, Func<Household, FamilyMember, Result> change, CancellationToken cancellationToken) =>
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The household was not saved.");
                // Members load in no particular order, so find the spouse by ID.
                var spouse = tracked.FindMember(household.Members[1].Id)
                    ?? throw new InvalidOperationException("The spouse was not saved.");
                TestData.Ok(change(tracked, spouse));
            },
            cancellationToken);

    [Fact]
    public async Task A_run_assigns_every_eligible_member_each_module_with_audit_and_outbox_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        var created = Only(run.Created, instrument);
        Assert.Equal(
            new[] { (household.Members[0].Id.Value, "A"), (household.Members[1].Id.Value, "B") }.Order(),
            created.Select(summary => (summary.MemberId, summary.SectionCode ?? "")).Order());
        Assert.All(created, summary => Assert.Equal(AssignmentReason.EligibilityRule, summary.Reason));
        Assert.Equal(6, run.Decisions.Count(decision => decision.InstrumentCode == instrument.Code));
        var child = run.Decisions.Where(decision => decision.MemberId == household.Members[2].Id.Value && decision.InstrumentCode == instrument.Code);
        Assert.All(child, decision => Assert.Equal(EligibilityReasons.MemberUnder18, decision.ReasonCode));

        foreach (var summary in created)
        {
            var saved = await fixture.QueryAsync(db => db.AssessmentAssignments.SingleAsync(
                assignment => assignment.Id == AssessmentAssignmentId.From(summary.AssignmentId), cancellationToken));
            Assert.Equal(AssignmentStatus.Assigned, saved.Status);
            Assert.Equal(wave.ClosesAt, saved.ExpiresAt);
            Assert.Equal(summary.EligibilityRuleSetId, saved.EligibilityRuleSetId.Value);
            Assert.Single(await fixture.OutboxMessagesAsync(
                "assignments.assessment_assigned.v1", "assessmentAssignmentId", summary.AssignmentId, cancellationToken));
            Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
                audit => audit.Action == "assignments.assignment.created" && audit.ResourceId == summary.AssignmentId.ToString(),
                cancellationToken)));
        }
    }

    [Fact]
    public async Task Running_again_creates_nothing_new()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);
        TestData.Ok(await RunAsync(household, wave, cancellationToken));

        var again = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        Assert.Empty(Only(again.Created, instrument));
        Assert.Equal(2, Only(again.Kept, instrument).Count);
    }

    [Fact]
    public async Task A_member_who_no_longer_qualifies_loses_a_not_started_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);
        TestData.Ok(await RunAsync(household, wave, cancellationToken));
        await ChangeSpouseAsync(
            household,
            (tracked, spouse) => tracked.UpdateMember(
                spouse.Id,
                new FamilyMemberDetails(spouse.BirthMonth, spouse.RelationshipToHousehold, EmploymentStatus.Unemployed, false, false),
                TestData.Now),
            cancellationToken);

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        var cancelled = Assert.Single(Only(run.Cancelled, instrument));
        Assert.Equal((household.Members[1].Id.Value, "B", AssignmentStatus.Cancelled), (cancelled.MemberId, cancelled.SectionCode, cancelled.Status));
        Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "assignments.assignment.cancelled" && audit.ResourceId == cancelled.AssignmentId.ToString(),
            cancellationToken)));
    }

    [Fact]
    public async Task A_deactivated_member_loses_a_not_started_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);
        TestData.Ok(await RunAsync(household, wave, cancellationToken));
        await ChangeSpouseAsync(household, (tracked, spouse) => tracked.DeactivateMember(spouse.Id, TestData.Now), cancellationToken);

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        Assert.Equal(household.Members[1].Id.Value, Assert.Single(Only(run.Cancelled, instrument)).MemberId);
        Assert.Contains(run.Decisions, decision => decision.MemberId == household.Members[1].Id.Value
            && decision.InstrumentCode == instrument.Code && decision.ReasonCode == EligibilityReasons.MemberInactive);
    }

    [Fact]
    public async Task A_started_assignment_is_kept_when_the_member_no_longer_qualifies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);
        var first = TestData.Ok(await RunAsync(household, wave, cancellationToken));
        var spouseAssignment = Only(first.Created, instrument).Single(summary => summary.SectionCode == "B");
        await fixture.InScopeAsync(
            async services =>
            {
                var assignment = await services.GetRequiredService<IAssessmentAssignmentRepository>()
                    .GetAsync(AssessmentAssignmentId.From(spouseAssignment.AssignmentId), cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                services.GetRequiredService<IAssessmentSessionRepository>().Add(TestData.Ok(AssessmentSession.Start(assignment, TestData.Now)));
            },
            cancellationToken);
        await ChangeSpouseAsync(
            household,
            (tracked, spouse) => tracked.UpdateMember(
                spouse.Id,
                new FamilyMemberDetails(spouse.BirthMonth, spouse.RelationshipToHousehold, EmploymentStatus.Retired, false, false),
                TestData.Now),
            cancellationToken);

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        Assert.Empty(Only(run.Cancelled, instrument));
        Assert.Contains(Only(run.Kept, instrument), summary => summary.AssignmentId == spouseAssignment.AssignmentId
            && summary.Status == AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task A_member_with_missing_employment_is_indeterminate_and_gets_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken, spouseEmployment: null);

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        var decision = run.Decisions.Single(candidate => candidate.MemberId == household.Members[1].Id.Value
            && candidate.InstrumentCode == instrument.Code && candidate.SectionCode == "B");
        Assert.Equal(EligibilityOutcome.Indeterminate, decision.Outcome);
        Assert.Null(decision.Eligible);
        Assert.Equal(new[] { MissingFacts.EmploymentStatus }, decision.MissingInformation);
        Assert.DoesNotContain(Only(run.Created, instrument), summary => summary.MemberId == household.Members[1].Id.Value);
    }

    [Fact]
    public async Task A_module_completed_under_an_older_version_is_not_assigned_again_under_a_newer_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);
        var first = TestData.Ok(await RunAsync(household, wave, cancellationToken));
        var headAssignment = Only(first.Created, instrument).Single(summary => summary.SectionCode == "A");
        await CompleteAsync(headAssignment.AssignmentId, cancellationToken);

        var switchover = TestData.Now.AddHours(-1);
        var oldVersionId = InstrumentVersionId.From(headAssignment.InstrumentVersionId);
        await fixture.InScopeAsync(
            async services =>
            {
                var old = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(oldVersionId, cancellationToken)
                    ?? throw new InvalidOperationException("The version was not saved.");
                TestData.Ok(old.Retire(switchover, TestData.Now));
            },
            cancellationToken);
        var replacement = await fixture.DraftVersionAsync(cancellationToken, instrument, versionNumber: 2);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(replacement.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The version was not saved.");
                TestData.Ok(tracked.Approve(instrument, TestData.TestApproval(), switchover, null, TestData.Now));
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(replacement.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The version was not saved.");
                var rules = TestData.Ok(RuleSet.Propose(
                    RuleSetKind.Eligibility, TestData.UniqueCode("eligibility"), 1, tracked,
                    $$"""{"schema":"eligibility.v1","target":{"sectionCode":"A"},"rule":{{HeadOrEarner}}}""", TestData.Now));
                TestData.Ok(rules.Approve(TestData.TestApproval(), tracked, TestData.Now));
                services.GetRequiredService<IRuleSetRepository>().Add(rules);
            },
            cancellationToken);

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        Assert.DoesNotContain(Only(run.Created, instrument), summary => summary.MemberId == household.Members[0].Id.Value);
        Assert.Contains(Only(run.Kept, instrument), summary => summary.AssignmentId == headAssignment.AssignmentId
            && summary.Status == AssignmentStatus.Completed);
    }

    [Fact]
    public async Task A_newer_version_approved_mid_wave_creates_no_duplicate_assignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);
        var first = TestData.Ok(await RunAsync(household, wave, cancellationToken));
        var headAssignment = Only(first.Created, instrument).Single(summary => summary.SectionCode == "A");
        var switchover = TestData.Now.AddHours(-1);
        await fixture.InScopeAsync(
            async services =>
            {
                var old = await services.GetRequiredService<IInstrumentVersionRepository>()
                    .GetAsync(InstrumentVersionId.From(headAssignment.InstrumentVersionId), cancellationToken)
                    ?? throw new InvalidOperationException("The version was not saved.");
                TestData.Ok(old.Retire(switchover, TestData.Now));
            },
            cancellationToken);
        var replacement = await fixture.DraftVersionAsync(cancellationToken, instrument, versionNumber: 2);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(replacement.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The version was not saved.");
                TestData.Ok(tracked.Approve(instrument, TestData.TestApproval(), switchover, null, TestData.Now));
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(replacement.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The version was not saved.");
                foreach (var section in new[] { "A", "B" })
                {
                    var rules = TestData.Ok(RuleSet.Propose(
                        RuleSetKind.Eligibility, TestData.UniqueCode("eligibility"), 1, tracked,
                        $$"""{"schema":"eligibility.v1","target":{"sectionCode":"{{section}}"},"rule":{{(section == "A" ? HeadOrEarner : WorkingParent)}}}""",
                        TestData.Now));
                    TestData.Ok(rules.Approve(TestData.TestApproval(), tracked, TestData.Now));
                    services.GetRequiredService<IRuleSetRepository>().Add(rules);
                }
            },
            cancellationToken);

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        Assert.Empty(Only(run.Created, instrument));
        Assert.Empty(Only(run.Cancelled, instrument));
        Assert.Equal(2, Only(run.Kept, instrument).Count);
        Assert.Contains(Only(run.Kept, instrument), summary => summary.AssignmentId == headAssignment.AssignmentId);
    }

    [Fact]
    public async Task Concurrent_runs_never_create_duplicate_assignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);

        var results = await Task.WhenAll(RunAsync(household, wave, cancellationToken), RunAsync(household, wave, cancellationToken));

        // The household row lock serialises the runs: both succeed and the later one finds the earlier one's work.
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(2, results.Sum(result => Only(result.Value.Created, instrument).Count));
        Assert.Equal(2, results.Sum(result => Only(result.Value.Kept, instrument).Count));
        // Earlier tests' instruments also assign this household, so count only this test's instrument.
        var versionIds = await fixture.QueryAsync(db => db.InstrumentVersions
            .Where(version => version.InstrumentId == instrument.Id).Select(version => version.Id).ToListAsync(cancellationToken));
        var saved = (await fixture.QueryAsync(db => db.AssessmentAssignments
                .Where(assignment => assignment.WaveId == wave.Id).ToListAsync(cancellationToken)))
            .Where(assignment => versionIds.Contains(assignment.InstrumentVersionId)
                && assignment.Status != AssignmentStatus.Cancelled && assignment.Status != AssignmentStatus.Expired)
            .ToList();
        Assert.Equal(2, saved.Count);
    }

    [Fact]
    public async Task A_member_eligible_for_several_modules_gets_each_in_one_run()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head(40), TestData.Child(10));
        var wave = await fixture.OpenWaveAsync(cancellationToken);
        var (instrument, _, _) = await fixture.EligibilityScenarioAsync(cancellationToken, ("A", HeadOrEarner), ("B", WorkingParent));

        var run = TestData.Ok(await RunAsync(household, wave, cancellationToken));

        Assert.Equal(
            new[] { "A", "B" },
            Only(run.Created, instrument).Where(summary => summary.MemberId == household.Members[0].Id.Value)
                .Select(summary => summary.SectionCode ?? "").Order());
        Assert.Equal(2, Only(run.Created, instrument).Count);
    }

    [Fact]
    public async Task Every_run_is_audited_as_an_eligibility_read_even_when_it_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, _) = await ArrangeAsync(cancellationToken);
        TestData.Ok(await RunAsync(household, wave, cancellationToken));
        var resource = household.Id.Value.ToString();
        var before = await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "assignments.eligibility.evaluated" && audit.ResourceId == resource, cancellationToken));

        TestData.Ok(await RunAsync(household, wave, cancellationToken));

        Assert.Equal(before + 1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "assignments.eligibility.evaluated" && audit.ResourceId == resource, cancellationToken)));
    }

    [Fact]
    public async Task A_withdrawn_household_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, _) = await ArrangeAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IHouseholdRepository>().GetAsync(household.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The household was not saved.");
                TestData.Ok(tracked.Withdraw(TestData.Now));
            },
            cancellationToken);

        Assert.Equal(HouseholdErrors.NotActive, (await RunAsync(household, wave, cancellationToken)).Error);
    }

    [Fact]
    public async Task A_closed_wave_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, _) = await ArrangeAsync(cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IWaveRepository>().GetAsync(wave.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The wave was not saved.");
                TestData.Ok(tracked.Close(TestData.Now));
            },
            cancellationToken);

        Assert.Equal(AssignmentErrors.WaveClosed, (await RunAsync(household, wave, cancellationToken)).Error);
    }

    [Fact]
    public async Task Two_approved_rules_for_the_same_module_stop_the_run()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var household = await fixture.SavedHouseholdAsync(cancellationToken, TestData.Head());
        var wave = await fixture.OpenWaveAsync(cancellationToken);
        var (_, version, rules) = await fixture.EligibilityScenarioAsync(cancellationToken, ("A", HeadOrEarner), ("A", """{"ageAtLeast":60}"""));
        try
        {
            Assert.Equal(EligibilityErrors.AmbiguousRules, (await RunAsync(household, wave, cancellationToken)).Error);
        }
        finally
        {
            // Leave no ambiguity behind for the other tests in this collection.
            await fixture.InScopeAsync(
                async services =>
                {
                    var tracked = await services.GetRequiredService<IRuleSetRepository>().GetAsync(rules[1].Id, cancellationToken)
                        ?? throw new InvalidOperationException("The rule set was not saved.");
                    TestData.Ok(tracked.Retire(TestData.Now));
                },
                cancellationToken);
        }

        Assert.True((await RunAsync(household, wave, cancellationToken)).IsSuccess, version.Id.ToString());
    }

    [Fact]
    public async Task Evaluating_eligibility_writes_no_assignment_and_records_an_audit_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (household, wave, instrument) = await ArrangeAsync(cancellationToken);

        await using var scope = fixture.CreateScope();
        var evaluation = TestData.Ok(await scope.ServiceProvider.GetRequiredService<EvaluateHouseholdEligibility>()
            .HandleAsync(household.Id.Value, new HouseholdWaveRequest(wave.Id.Value), cancellationToken));

        Assert.Equal(2, evaluation.WouldCreate.Count(planned => planned.InstrumentCode == instrument.Code));
        Assert.Equal(0, await fixture.QueryAsync(db => db.AssessmentAssignments.CountAsync(
            assignment => assignment.WaveId == wave.Id, cancellationToken)));
        Assert.Equal(1, await fixture.QueryAsync(db => db.AuditEvents.CountAsync(
            audit => audit.Action == "assignments.eligibility.evaluated" && audit.ResourceId == household.Id.Value.ToString(),
            cancellationToken)));
    }

    /// <summary>Starts, answers (every item of the assignment's section) and submits, in separate commits.</summary>
    private async Task CompleteAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var sessionId = await fixture.InScopeAsync(
            async services =>
            {
                var assignment = await services.GetRequiredService<IAssessmentAssignmentRepository>()
                    .GetAsync(AssessmentAssignmentId.From(assignmentId), cancellationToken) ?? throw new InvalidOperationException("Missing assignment.");
                var session = TestData.Ok(AssessmentSession.Start(assignment, TestData.Now));
                services.GetRequiredService<IAssessmentSessionRepository>().Add(session);
                return session.Id;
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("Missing session.");
                var version = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(session.InstrumentVersionId, cancellationToken)
                    ?? throw new InvalidOperationException("Missing version.");
                foreach (var item in version.GetItems().Where(item => item.InstrumentSectionId == session.InstrumentSectionId))
                {
                    TestData.Ok(session.Answer(version, item.Id, item.Options[0].Id, TestData.Now));
                }
            },
            cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var session = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(sessionId, cancellationToken)
                    ?? throw new InvalidOperationException("Missing session.");
                var version = await services.GetRequiredService<IInstrumentVersionRepository>().GetAsync(session.InstrumentVersionId, cancellationToken)
                    ?? throw new InvalidOperationException("Missing version.");
                var assignment = await services.GetRequiredService<IAssessmentAssignmentRepository>()
                    .GetAsync(session.AssessmentAssignmentId, cancellationToken) ?? throw new InvalidOperationException("Missing assignment.");
                TestData.Ok(session.Submit(version, assignment, TestData.Now));
            },
            cancellationToken);
    }
}
