using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// Synthetic data for local development only: three nameless households, a planned wave, draft consent policies, the
/// 39-item household assessment as a draft with the brief's scoring as a proposal, and the Abu Dhabi Family Survey
/// codebook as a draft with its proposed eligibility rules. Nothing is approved, so nothing can be assigned, answered
/// or scored. The API refuses to run this outside the Development environment (<c>db seed</c>). A database seeded
/// before this change needs <c>npm run db:reset</c>.
/// </summary>
public static class DevelopmentDataSeeder
{
    public const string WaveCode = "dev_synthetic_wave_1";
    public const string InstrumentCode = "household_assessment";
    public const string ScoringRuleSetCode = "household_assessment_total";
    public const string FamilyVoicesInstrumentCode = "abu_dhabi_family_survey";
    public const string PlaceholderDocument = "dev-placeholder";

    /// <summary>Seeds once in one transaction. Returns false when the seed wave already exists.</summary>
    public static async Task<bool> SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var waves = provider.GetRequiredService<IWaveRepository>();
        if (await waves.GetByCodeAsync(WaveCode, cancellationToken) is not null)
        {
            return false;
        }

        var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var households = provider.GetRequiredService<IHouseholdRepository>();
        foreach (var roster in Rosters(today))
        {
            households.Add(Required(Household.Create(HouseholdReference.Generate(), roster, now)));
        }

        waves.Add(Required(Wave.Plan(WaveCode, now.AddDays(30), now.AddDays(120), now)));

        var policies = provider.GetRequiredService<IConsentPolicyVersionRepository>();
        foreach (var type in Enum.GetValues<ConsentType>())
        {
            policies.Add(Required(ConsentPolicyVersion.CreateDraft(type, 1, PlaceholderDocument, PlaceholderDocument, now)));
        }

        SeedHouseholdAssessmentDraft(provider, now);
        SeedFamilyVoicesDraft(provider, now);

        var committed = await provider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            throw new InvalidOperationException($"Seeding development data failed: {committed.Error!.Code}");
        }

        return true;
    }

    /// <summary>
    /// The 39-item household assessment as draft version 1, copied from the demo, and the brief's scoring as a Proposed
    /// rule set. Nothing is approved: a named approver must confirm the content and the bands (open questions 1 and 2)
    /// through the API before anything can be assigned or scored.
    /// </summary>
    private static void SeedHouseholdAssessmentDraft(IServiceProvider provider, DateTimeOffset now)
    {
        var package = ContentFiles.ReadPackage(ContentFiles.HouseholdAssessment);
        var instrument = Required(InstrumentContentImporter.CreateInstrument(package.Instrument, now));
        var draft = Required(InstrumentContentImporter.BuildDraft(instrument, 1, package.Version, now));
        provider.GetRequiredService<IInstrumentRepository>().Add(instrument);
        provider.GetRequiredService<IInstrumentVersionRepository>().Add(draft);

        var validator = provider.GetRequiredService<IEnumerable<IRuleDefinitionValidator>>()
            .Single(candidate => candidate.Kind == RuleSetKind.Scoring);
        var ruleSets = provider.GetRequiredService<IRuleSetRepository>();
        foreach (var (code, fileName) in ContentFiles.ProposedScoringRules)
        {
            var definition = ContentFiles.Read(fileName);
            Ensure(validator.Validate(definition, instrument, draft));
            ruleSets.Add(Required(RuleSet.Propose(RuleSetKind.Scoring, code, 1, draft, definition, now)));
        }
    }

    /// <summary>
    /// The Abu Dhabi Family Survey codebook as draft version 1 (Arabic gaps and all) and the three proposed eligibility
    /// rule sets. Nothing is approved: a named approver must complete the Arabic and approve it through the API.
    /// </summary>
    private static void SeedFamilyVoicesDraft(IServiceProvider provider, DateTimeOffset now)
    {
        var package = ContentFiles.ReadPackage(ContentFiles.AbuDhabiFamilySurvey);
        var instrument = Required(InstrumentContentImporter.CreateInstrument(package.Instrument, now));
        var draft = Required(InstrumentContentImporter.BuildDraft(instrument, 1, package.Version, now));
        provider.GetRequiredService<IInstrumentRepository>().Add(instrument);
        provider.GetRequiredService<IInstrumentVersionRepository>().Add(draft);

        var validator = new EligibilityDefinitionValidator();
        var ruleSets = provider.GetRequiredService<IRuleSetRepository>();
        foreach (var (code, fileName) in ContentFiles.ProposedEligibilityRules)
        {
            var definition = ContentFiles.Read(fileName);
            Ensure(validator.Validate(definition, instrument, draft));
            ruleSets.Add(Required(RuleSet.Propose(RuleSetKind.Eligibility, code, 1, draft, definition, now)));
        }
    }

    private static IEnumerable<FamilyMemberDetails[]> Rosters(DateOnly today)
    {
        yield return
        [
            Member(today, 42, null, EmploymentStatus.Employed, isHead: true, isPrimaryEarner: true),
            Member(today, 39, HouseholdRelationship.Spouse, EmploymentStatus.Employed),
            Member(today, 9, HouseholdRelationship.Child, null),
            Member(today, 15, HouseholdRelationship.Child, null),
        ];
        yield return
        [
            Member(today, 47, null, EmploymentStatus.SelfEmployed, isHead: true, isPrimaryEarner: true),
            Member(today, 68, HouseholdRelationship.Parent, EmploymentStatus.Retired),
            Member(today, 22, HouseholdRelationship.Child, EmploymentStatus.Student),
        ];
        yield return
        [
            Member(today, 30, null, EmploymentStatus.Employed, isHead: true, isPrimaryEarner: true),
        ];
    }

    private static FamilyMemberDetails Member(
        DateOnly today,
        int age,
        HouseholdRelationship? relationship,
        EmploymentStatus? employment,
        bool isHead = false,
        bool isPrimaryEarner = false) =>
        new(Required(YearMonth.Create(today.Year - age, today.Month, today)), relationship, employment, isHead, isPrimaryEarner);

    private static T Required<T>(Result<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Seed data is invalid: {result.Error!.Code}");

    private static void Ensure(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Seed data is invalid: {result.Error!.Code}");
        }
    }
}
