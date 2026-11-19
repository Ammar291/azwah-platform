using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Persistence.Seeding;
using FamilyResilience.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.IntegrationTests.Persistence;

/// <summary>The only test that seeds the shared database, so the first call must seed and the second must not.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class DevelopmentDataSeederTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Seeding_adds_synthetic_data_once_and_approves_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var householdsBefore = await fixture.QueryAsync(db => db.Households.CountAsync(cancellationToken));

        var first = await DevelopmentDataSeeder.SeedAsync(fixture.Services, cancellationToken);
        var householdsAfterFirst = await fixture.QueryAsync(db => db.Households.CountAsync(cancellationToken));
        var second = await DevelopmentDataSeeder.SeedAsync(fixture.Services, cancellationToken);
        var householdsAfterSecond = await fixture.QueryAsync(db => db.Households.CountAsync(cancellationToken));

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(householdsBefore + 3, householdsAfterFirst);
        Assert.Equal(householdsAfterFirst, householdsAfterSecond);

        var wave = await fixture.QueryAsync(db => db.Waves.SingleAsync(w => w.Code == DevelopmentDataSeeder.WaveCode, cancellationToken));
        Assert.Equal(WaveStatus.Planned, wave.Status);

        var instrument = await fixture.QueryAsync(db =>
            db.Instruments.SingleAsync(i => i.Code == DevelopmentDataSeeder.InstrumentCode, cancellationToken));
        Assert.Equal("household_assessment", instrument.Code);
        Assert.Equal(InstrumentKind.HouseholdAssessment, instrument.Kind);
        Assert.StartsWith("Family Resilience Assessment", instrument.Name.English, StringComparison.Ordinal);
        var assessmentVersion = await fixture.QueryAsync(db => db.InstrumentVersions
            .Include(v => v.Sections).ThenInclude(s => s.Items)
            .SingleAsync(v => v.InstrumentId == instrument.Id, cancellationToken));
        Assert.Equal(InstrumentVersionStatus.Draft, assessmentVersion.Status);
        Assert.Equal(7, assessmentVersion.Sections.Count);
        Assert.Equal(39, assessmentVersion.GetItems().Count());
        var scoringProposal = await fixture.QueryAsync(db =>
            db.RuleSets.SingleAsync(r => r.InstrumentVersionId == assessmentVersion.Id, cancellationToken));
        Assert.Equal((RuleSetKind.Scoring, DevelopmentDataSeeder.ScoringRuleSetCode, 1, RuleSetStatus.Proposed),
            (scoringProposal.Kind, scoringProposal.Code, scoringProposal.VersionNumber, scoringProposal.Status));

        var policies = await fixture.QueryAsync(db =>
            db.ConsentPolicyVersions
                .Where(p => p.DocumentReferenceEn == DevelopmentDataSeeder.PlaceholderDocument)
                .ToListAsync(cancellationToken));
        Assert.Equal(3, policies.Count);
        Assert.All(policies, policy => Assert.Equal(ConsentPolicyStatus.Draft, policy.Status));

        var survey = await fixture.QueryAsync(db =>
            db.Instruments.SingleAsync(i => i.Code == DevelopmentDataSeeder.FamilyVoicesInstrumentCode, cancellationToken));
        var surveyVersion = await fixture.QueryAsync(db => db.InstrumentVersions
            .Include(v => v.Sections).ThenInclude(s => s.Items)
            .SingleAsync(v => v.InstrumentId == survey.Id, cancellationToken));
        Assert.Equal(InstrumentKind.FamilyVoices, survey.Kind);
        Assert.Equal(InstrumentVersionStatus.Draft, surveyVersion.Status);
        Assert.Equal(36, surveyVersion.GetItems().Count());
        var proposals = await fixture.QueryAsync(db =>
            db.RuleSets.Where(r => r.InstrumentVersionId == surveyVersion.Id).ToListAsync(cancellationToken));
        Assert.Equal(
            new[] { "family_voices_module_a", "family_voices_module_b", "family_voices_module_c" },
            proposals.Select(r => r.Code).Order(StringComparer.Ordinal));
        Assert.All(proposals, proposal =>
        {
            Assert.Equal(RuleSetKind.Eligibility, proposal.Kind);
            Assert.Equal(RuleSetStatus.Proposed, proposal.Status);
        });
    }
}
