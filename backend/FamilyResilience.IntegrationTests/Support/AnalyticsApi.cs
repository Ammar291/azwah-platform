using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// An API host with its own database holding a known synthetic population, so aggregate counts can be asserted exactly.
/// Wave one: 12 heads aged 42 (employed, completed), 10 aged 50 (employed, 7 completed) and 2 aged 65 (retired,
/// completed). Wave two: one head aged 42, not started. Every completion happens at <see cref="TestData.Now"/>, and every
/// completed session answers the version's four items with their first option in display order (value 1), so each
/// complete result totals 4.
/// Completed sessions are scored with <see cref="Scoring"/>: of the 42-year-olds, 11 reach tier <c>band_high</c> and one is
/// incomplete; the 50-year-olds reach <c>band_low</c>; the 65-year-olds reach <c>band_mid</c> and were also scored with a
/// rule set that has since been retired. Everyone completed has research-use consent except two 50-year-olds, who have
/// participation consent and a research-use consent they withdrew. Wave three: one household of a head aged 42 and a
/// spouse aged 39; the head completed, was scored with <c>band_high</c> and gave research-use consent, then the household
/// withdrew from the study. Research outputs leave it out, and its two members, still Active rows, are not counted as
/// active members. Wave four: 4 heads aged 25 (students), each holding three assignments that were never started (the
/// whole version and modules A and B): 12 assignments of 4 people, so their cells are suppressed although 12 is not
/// under the minimum of 10 (D-58 counts people).
/// In all: 30 households (29 active, 1 withdrawn), 29 active members of active households, 22 sessions (all of them
/// submitted and scored; waves one and three only) and 38 assignments (24, 1, 1 and 12 by wave).
/// </summary>
public sealed class AnalyticsApi : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new();

    public InstrumentVersion Version { get; private set; } = null!;

    public Wave WaveOne { get; private set; } = null!;

    public Wave WaveTwo { get; private set; } = null!;

    public Wave WaveThree { get; private set; } = null!;

    public Wave WaveFour { get; private set; } = null!;

    /// <summary>The approved scoring rule set every completed session was scored with.</summary>
    public RuleSet Scoring { get; private set; } = null!;

    private RuleSet _retiredScoring = null!;

    private ConsentPolicyVersion _researchPolicy = null!;

    private ConsentPolicyVersion _participationPolicy = null!;

    private sealed record Seed(int Age, EmploymentStatus Employment, bool Completed, string? Tier, bool ResearchConsent, bool AlsoRetired = false);

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.InitializeAsync();
        Version = await Factory.ApprovedVersionAsync(cancellationToken);
        WaveOne = await Factory.OpenWaveAsync(cancellationToken);
        WaveTwo = await Factory.OpenWaveAsync(cancellationToken);
        WaveThree = await Factory.OpenWaveAsync(cancellationToken);
        WaveFour = await Factory.OpenWaveAsync(cancellationToken);
        Scoring = await Factory.ApprovedRuleSetAsync(RuleSetKind.Scoring, Version, cancellationToken);
        _retiredScoring = await Factory.ApprovedRuleSetAsync(RuleSetKind.Scoring, Version, cancellationToken);
        _researchPolicy = await Factory.ApprovedPolicyAsync(ConsentType.ResearchUse, cancellationToken);
        _participationPolicy = await Factory.ApprovedPolicyAsync(ConsentType.Participation, cancellationToken);

        foreach (var (seed, count) in new[]
                 {
                     (new Seed(42, EmploymentStatus.Employed, true, "band_high", true), 11),
                     (new Seed(42, EmploymentStatus.Employed, true, null, true), 1),
                     (new Seed(50, EmploymentStatus.Employed, true, "band_low", true), 5),
                     (new Seed(50, EmploymentStatus.Employed, true, "band_low", false), 2),
                     (new Seed(50, EmploymentStatus.Employed, false, null, false), 3),
                     (new Seed(65, EmploymentStatus.Retired, true, "band_mid", true, AlsoRetired: true), 2),
                 })
        {
            for (var index = 0; index < count; index++)
            {
                await SeedAsync(WaveOne, seed, cancellationToken);
            }
        }

        await SeedAsync(WaveTwo, new Seed(42, EmploymentStatus.Employed, false, null, false), cancellationToken);
        var withdrawn = await SeedAsync(
            WaveThree, new Seed(42, EmploymentStatus.Employed, true, "band_high", true), cancellationToken, TestData.Spouse());
        for (var index = 0; index < 4; index++)
        {
            var student = await Factory.SavedHouseholdAsync(
                cancellationToken, new FamilyMemberDetails(TestData.BornYearsAgo(25), null, EmploymentStatus.Student, true, true));
            foreach (var sectionId in new InstrumentSectionId?[] { null, Version.Sections[0].Id, Version.Sections[1].Id })
            {
                await Factory.AssignmentAsync(student.Members[0], Version, cancellationToken, sectionId, WaveFour);
            }
        }

        await Factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IHouseholdRepository>().GetAsync(withdrawn.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The household was not saved.");
                TestData.Ok(tracked.Withdraw(TestData.Now.AddMinutes(4)));
            },
            cancellationToken);
        await Factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IRuleSetRepository>().GetAsync(_retiredScoring.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The rule set was not saved.");
                TestData.Ok(tracked.Retire(TestData.Now.AddMinutes(2)));
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();

    /// <summary>
    /// The item's first option in display order (value 1 in the synthetic version). A version read back from the database
    /// holds its options in no promised order: the repository's split query sorts by version, section and item only, so
    /// <c>Options[0]</c> could be any option and the population's totals would change from run to run.
    /// </summary>
    private static ResponseOption FirstOption(InstrumentItem item) => item.Options.OrderBy(option => option.DisplayOrder).First();

    /// <summary>
    /// A household of a head (and <paramref name="others"/>, who are assigned nothing) whose head is assigned the version
    /// in <paramref name="wave"/>; when the seed is completed, answered, submitted and scored, and given research-use
    /// consent when the seed says so.
    /// </summary>
    private async Task<Household> SeedAsync(Wave wave, Seed seed, CancellationToken cancellationToken, params FamilyMemberDetails[] others)
    {
        var head = new FamilyMemberDetails(TestData.BornYearsAgo(seed.Age), null, seed.Employment, true, true);
        var household = await Factory.SavedHouseholdAsync(cancellationToken, [head, .. others]);
        var member = household.Members[0];
        var assignment = await Factory.AssignmentAsync(member, Version, cancellationToken, wave: wave);
        if (!seed.Completed)
        {
            return household;
        }

        var session = await Factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                var started = TestData.Ok(AssessmentSession.Start(tracked, TestData.Now));
                foreach (var item in Version.GetItems())
                {
                    TestData.Ok(started.Answer(Version, item.Id, FirstOption(item).Id, TestData.Now));
                }

                services.GetRequiredService<IAssessmentSessionRepository>().Add(started);
                return started.Id;
            },
            cancellationToken);

        // Answering, submitting and scoring are separate commits (triggers check the session's status per statement).
        await Factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session, cancellationToken)
                    ?? throw new InvalidOperationException("The session was not saved.");
                var trackedAssignment = await services.GetRequiredService<IAssessmentAssignmentRepository>().GetAsync(assignment.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The assignment was not saved.");
                TestData.Ok(tracked.Submit(Version, trackedAssignment, TestData.Now));
            },
            cancellationToken);

        foreach (var rules in seed.AlsoRetired ? [Scoring, _retiredScoring] : new[] { Scoring })
        {
            await Factory.InScopeAsync(
                async services =>
                {
                    var tracked = await services.GetRequiredService<IAssessmentSessionRepository>().GetAsync(session, cancellationToken)
                        ?? throw new InvalidOperationException("The session was not saved.");
                    var computation = seed.Tier is null
                        ? TestData.IncompleteComputation(tracked)
                        : TestData.SyntheticComputation(tracked, seed.Tier);
                    services.GetRequiredService<IScoreResultRepository>().Add(
                        TestData.Ok(ScoreResult.Record(tracked, rules, computation, TestData.Now.AddMinutes(1))));
                },
                cancellationToken);
        }

        if (seed.ResearchConsent)
        {
            await Factory.GrantedConsentAsync(member, _researchPolicy, cancellationToken);
            return household;
        }

        // Consent of another type, or research-use consent that was withdrawn, must both leave the member out.
        await Factory.GrantedConsentAsync(member, _participationPolicy, cancellationToken);
        var withdrawn = await Factory.GrantedConsentAsync(member, _researchPolicy, cancellationToken);
        await Factory.InScopeAsync(
            async services =>
            {
                var tracked = await services.GetRequiredService<IConsentRecordRepository>().GetAsync(withdrawn.Id, cancellationToken)
                    ?? throw new InvalidOperationException("The consent was not saved.");
                TestData.Ok(tracked.Withdraw("synthetic-participant", TestData.Now.AddMinutes(3)));
            },
            cancellationToken);
        return household;
    }
}
