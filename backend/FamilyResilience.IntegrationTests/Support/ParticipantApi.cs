using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>
/// An API host with its own database, seeded once with synthetic approved content so participant journeys can run end
/// to end: a family-voices instrument with sections A and B, eligibility rules (A: head of household; B: aged 60 or
/// over), one open wave, approved consent policies of every type, and scoring and referral rule sets for section A
/// (section B has none, so its sessions stay unscored). Only this
/// throwaway database ever holds approved synthetic content. Tests share it, so they must not add approved eligibility
/// rules or open waves, and must not assume empty tables.
/// </summary>
public sealed class ParticipantApi : IAsyncLifetime
{
    /// <summary>Section A totals run from 2 to 6: answering every A item with its lowest option (1) gives band_low.</summary>
    public const string SectionAScoring =
        """{"schema":"scoring.v1","target":{"sectionCode":"A"},"policy":"summed_item_values","algorithmVersion":1,"parameters":{"reverseScoring":"mirror_scale","unanswered":"reject","total":true,"tierBands":[{"code":"band_low","min":2,"max":3},{"code":"band_high","min":4,"max":6}]}}""";

    /// <summary>Section A's band_low routes to the synthetic support category; band_high routes nowhere.</summary>
    public const string SectionAReferral =
        """{"schema":"referral.v1","target":{"sectionCode":"A"},"routes":[{"categoryCode":"synthetic_support","when":{"tierCodeIn":["band_low"]}}]}""";

    public ApiFactory Factory { get; } = new();

    public RuleSet ReferralRules { get; private set; } = null!;

    public Instrument Instrument { get; private set; } = null!;

    public InstrumentVersion Version { get; private set; } = null!;

    public Wave Wave { get; private set; } = null!;

    public RuleSet ScoringRules { get; private set; } = null!;

    public IReadOnlyDictionary<ConsentType, ConsentPolicyVersion> Policies { get; private set; } = null!;

    public InstrumentSection Section(string code) => Version.Sections.Single(section => section.Code == code);

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.InitializeAsync();

        (Instrument, Version, _) = await Factory.EligibilityScenarioAsync(
            cancellationToken, ("A", """{"isHeadOfHousehold":true}"""), ("B", """{"ageAtLeast":60}"""));
        Wave = await Factory.OpenWaveAsync(cancellationToken);
        var policies = new Dictionary<ConsentType, ConsentPolicyVersion>();
        foreach (var type in Enum.GetValues<ConsentType>())
        {
            policies[type] = await Factory.ApprovedPolicyAsync(type, cancellationToken);
        }

        Policies = policies;
        ScoringRules = TestData.Ok(RuleSet.Propose(RuleSetKind.Scoring, "section_a_scoring", 1, Version, SectionAScoring, TestData.Now));
        TestData.Ok(ScoringRules.Approve(TestData.TestApproval(), Version, TestData.Now));
        ReferralRules = TestData.Ok(RuleSet.Propose(RuleSetKind.Referral, "section_a_referral", 1, Version, SectionAReferral, TestData.Now));
        TestData.Ok(ReferralRules.Approve(TestData.TestApproval(), Version, TestData.Now));
        await Factory.InScopeAsync(
            services =>
            {
                var ruleSets = services.GetRequiredService<IRuleSetRepository>();
                ruleSets.Add(ScoringRules);
                ruleSets.Add(ReferralRules);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
