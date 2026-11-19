using CsCheck;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Content;
using static FamilyResilience.UnitTests.Domain.DomainTestData;

namespace FamilyResilience.UnitTests.Domain.Assignments.Eligibility;

public sealed class EligibilityPropertyTests
{
    private static readonly EligibilityRule ModuleB = new(
        RuleSetId.New(), "family_voices_module_b", 1, "abu_dhabi_family_survey", "B", null,
        Ok(EligibilityDefinitionParser.Parse(ContentFiles.Read(ContentFiles.FamilyVoicesModuleB))).Rule);

    private static readonly Gen<EligibilityFacts> FactsWithoutEmployment =
        from age in Gen.Int[18, 90]
        from head in Gen.Bool
        from earner in Gen.Bool
        from otherAges in Gen.Int[0, 90].Array[0, 6]
        select new EligibilityFacts(FamilyMemberId.New(), true, age, true, null, head, earner, [age, .. otherAges]);

    [Fact]
    public void Filling_in_a_missing_employment_status_never_changes_a_decided_outcome() =>
        FactsWithoutEmployment.Sample(facts =>
        {
            var withoutEmployment = EligibilityEvaluator.Evaluate(ModuleB, facts, Now).Outcome;
            if (withoutEmployment == EligibilityOutcome.Indeterminate)
            {
                return;
            }

            foreach (var status in Enum.GetValues<EmploymentStatus>())
            {
                Assert.Equal(withoutEmployment, EligibilityEvaluator.Evaluate(ModuleB, facts with { EmploymentStatus = status }, Now).Outcome);
            }
        });

    [Fact]
    public void An_indeterminate_outcome_always_names_the_missing_fact() =>
        FactsWithoutEmployment.Sample(facts =>
        {
            var decision = EligibilityEvaluator.Evaluate(ModuleB, facts, Now);
            if (decision.Outcome == EligibilityOutcome.Indeterminate)
            {
                Assert.Equal(new[] { MissingFacts.EmploymentStatus }, decision.MissingInformation);
            }
        });
}
