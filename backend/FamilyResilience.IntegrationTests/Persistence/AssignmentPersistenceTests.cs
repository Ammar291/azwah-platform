using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyResilience.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class AssignmentPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Assignment_round_trips_with_its_wave_section_and_rule_set()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var sectionId = version.Sections.Single(section => section.Code == "B").Id;
        var assignment = await fixture.AssignmentAsync(member, version, cancellationToken, sectionId);

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken),
            cancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(member.Id, loaded.FamilyMemberId);
        Assert.Equal(version.Id, loaded.InstrumentVersionId);
        Assert.Equal(sectionId, loaded.InstrumentSectionId);
        Assert.Equal(assignment.WaveId, loaded.WaveId);
        Assert.Equal(assignment.EligibilityRuleSetId, loaded.EligibilityRuleSetId);
        Assert.Equal(AssignmentStatus.Assigned, loaded.Status);
        Assert.Equal(AssignmentReason.EligibilityRule, loaded.Reason);
    }

    [Fact]
    public async Task Wave_is_found_by_code()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wave = await fixture.OpenWaveAsync(cancellationToken);

        var loaded = await fixture.InScopeAsync(
            services => services.GetRequiredService<IWaveRepository>().GetByCodeAsync(wave.Code, cancellationToken),
            cancellationToken);

        Assert.Equal(wave.Id, loaded?.Id);
        Assert.Equal(WaveStatus.Open, loaded?.Status);
    }

    [Fact]
    public async Task A_second_live_whole_version_assignment_in_the_same_wave_returns_already_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var first = await fixture.AssignmentAsync(member, version, cancellationToken);
        var wave = await fixture.InScopeAsync(
            services => services.GetRequiredService<IWaveRepository>().GetAsync(first.WaveId, cancellationToken),
            cancellationToken);
        Assert.NotNull(wave);
        var rules = await fixture.ApprovedRuleSetAsync(RuleSetKind.Eligibility, version, cancellationToken);

        await using var scope = fixture.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAssessmentAssignmentRepository>().Add(TestData.Ok(AssessmentAssignment.Assign(
            member, version, null, wave, rules, AssignmentReason.EligibilityRule, null, TestData.Now)));
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        Assert.Equal(AssignmentErrors.AlreadyAssigned, result.Error);
    }

    [Fact]
    public async Task A_cancelled_assignment_can_be_reissued_and_live_checks_follow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var first = await fixture.AssignmentAsync(member, version, cancellationToken);
        await fixture.InScopeAsync(
            async services =>
            {
                var loaded = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(first.Id, cancellationToken);
                Assert.NotNull(loaded);
                TestData.Ok(loaded.Cancel(TestData.Now));
            },
            cancellationToken);

        var liveAfterCancel = await fixture.InScopeAsync(
            services => services.GetRequiredService<IAssessmentAssignmentRepository>()
                .ExistsLiveAsync(member.Id, version.Id, null, first.WaveId, cancellationToken),
            cancellationToken);
        var wave = await fixture.InScopeAsync(
            services => services.GetRequiredService<IWaveRepository>().GetAsync(first.WaveId, cancellationToken),
            cancellationToken);
        Assert.NotNull(wave);
        await fixture.AssignmentAsync(member, version, cancellationToken, wave: wave);
        var all = await fixture.InScopeAsync(
            services => services.GetRequiredService<IAssessmentAssignmentRepository>().ListForMemberAsync(member.Id, first.WaveId, cancellationToken),
            cancellationToken);

        Assert.False(liveAfterCancel);
        Assert.Equal(2, all.Count);
        Assert.Single(all, assignment => assignment.Status == AssignmentStatus.Assigned);
    }

    [Fact]
    public async Task Database_rejects_an_eligibility_rule_set_of_another_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var assignmentId = (await fixture.AssignmentAsync(member, version, cancellationToken)).Id.Value;
        var otherVersion = await fixture.ApprovedVersionAsync(cancellationToken);
        var otherRulesId = (await fixture.ApprovedRuleSetAsync(RuleSetKind.Eligibility, otherVersion, cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE assessment_assignments SET eligibility_rule_set_id = {otherRulesId} WHERE id = {assignmentId}",
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Database_rejects_a_section_of_another_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = (await fixture.SavedHouseholdAsync(cancellationToken)).Members[0];
        var version = await fixture.ApprovedVersionAsync(cancellationToken);
        var assignmentId = (await fixture.AssignmentAsync(member, version, cancellationToken)).Id.Value;
        var otherSectionId = (await fixture.ApprovedVersionAsync(cancellationToken)).Sections[0].Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync(
                $"UPDATE assessment_assignments SET instrument_section_id = {otherSectionId} WHERE id = {assignmentId}",
                cancellationToken),
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Database_rejects_a_wave_that_closes_before_it_opens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var waveId = (await fixture.OpenWaveAsync(cancellationToken)).Id.Value;

        await PostgresAssert.RejectsAsync(
            () => fixture.ExecuteSqlAsync($"UPDATE waves SET closes_at = opens_at WHERE id = {waveId}", cancellationToken),
            PostgresErrorCodes.CheckViolation,
            "ck_waves_window");
    }
}
