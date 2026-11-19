using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Content;
using FamilyResilience.Infrastructure.Persistence.Seeding;

namespace FamilyResilience.UnitTests.Infrastructure;

/// <summary>The demonstration's synthetic population (D-140): fixed by its seed, nameless and spread for the analytics.</summary>
public sealed class DemonstrationPopulationTests
{
    private static readonly DateTimeOffset Built = new(2026, 11, 10, 7, 30, 15, TimeSpan.Zero);

    private static readonly DemonstrationPlan Plan = DemonstrationPopulation.Build(Built);

    [Fact]
    public void The_population_is_the_same_whatever_day_it_is_built_on_apart_from_the_dates()
    {
        static object Shape(DemonstrationPlan plan) => plan.Households.Select(household => new
        {
            household.Number,
            household.Reference,
            Members = string.Join(";", household.Members),
            Consenting = string.Join(",", household.ConsentingMembers),
            household.ResearchUse,
            household.IsLeadPersonaHousehold,
            WaveOne = Sitting(household.WaveOne),
            WaveTwo = Sitting(household.WaveTwo),
            HeadChange = household.HeadChange?.NewHeadIndex,
            Withdraws = household.WithdrawnAt is not null,
            Confirmed = household.RosterConfirmedAt is not null,
        }).ToList();

        static string? Sitting(DemonstrationSitting? sitting) =>
            sitting is null ? null : $"{sitting.Outcome}:{string.Join(",", sitting.Answers.Select(answer => $"{answer.ItemCode}={answer.RawValue}"))}";

        var elsewhen = DemonstrationPopulation.Build(new DateTimeOffset(2027, 3, 2, 18, 4, 0, TimeSpan.Zero));

        Assert.Equal(Shape(Plan), Shape(elsewhen));
        Assert.Equal(Built.AddDays(-210), Plan.Timeline.WaveOneOpens);
    }

    [Fact]
    public void Sixty_households_each_enrol_with_one_adult_head_and_an_employment_status_for_every_adult()
    {
        Assert.Equal(60, Plan.Households.Count);
        Assert.Equal(60, Plan.Households.Select(household => household.Reference).Distinct().Count());
        Assert.All(Plan.Households, household =>
        {
            Assert.Matches("^HH-DM[0-9]{2}-[0-9A-HJKMNP-TV-Z]{4}$", household.Reference);
            var head = Assert.Single(household.Members, member => member.IsHead);
            Assert.Same(household.Members[0], head);
            Assert.Equal((HouseholdRelationship?)null, head.Relationship);
            Assert.True(head.Age >= 31);
            Assert.True(household.Members.Count(member => member.IsPrimaryEarner) <= 1);
            Assert.All(household.Members.Where(member => !member.IsHead), member => Assert.NotNull(member.Relationship));
            Assert.All(household.Members, member =>
            {
                Assert.True(member.Age is < 17 or > 19, "No member is 17 to 19, so ages and employment hold from enrolment.");
                Assert.Equal(member.Age >= 18, member.Employment is not null);
            });
        });
    }

    [Fact]
    public void The_lead_persona_household_mirrors_the_mobile_fixtures_household_lead()
    {
        var lead = Assert.Single(Plan.Households, household => household.IsLeadPersonaHousehold);

        Assert.Equal(
            [(42, EmploymentStatus.Employed, true), (39, EmploymentStatus.Homemaker, false)],
            lead.Members.Where(member => member.Age >= 18).Select(member => (member.Age, member.Employment!.Value, member.IsHead)));
        Assert.Equal(DemonstrationOutcome.Completed, lead.WaveOne!.Outcome);
        Assert.Equal(DemonstrationOutcome.NotStarted, lead.WaveTwo!.Outcome);
        Assert.True(lead.ResearchUse);
    }

    /// <summary>The expected tier is computed here from the content files, as a forward or mirrored sum of the planned answers.</summary>
    [Fact]
    public void Every_wave_aims_at_least_ten_research_consented_heads_at_each_tier_and_heads_hold_at_least_ten_per_group()
    {
        var package = ContentFiles.ReadPackage(ContentFiles.HouseholdAssessment);
        var reverse = package.Version.Sections.SelectMany(section => section.Items)
            .ToDictionary(item => item.Code, item => item.ScoringDirection == ScoringDirection.Reverse, StringComparer.Ordinal);
        static string Tier(int total) => total switch { <= 78 => "vulnerable", <= 117 => "transitional", _ => "flourishing" };

        foreach (var wave in new Func<DemonstrationHousehold, DemonstrationSitting?>[] { household => household.WaveOne, household => household.WaveTwo })
        {
            var completed = Plan.Households
                .Where(household => wave(household) is { Outcome: DemonstrationOutcome.Completed })
                .ToList();
            var counted = completed.Where(household => household.ResearchUse && household.WithdrawnAt is null).ToList();
            var tiers = counted
                .GroupBy(household => Tier(wave(household)!.Answers.Sum(answer => reverse[answer.ItemCode] ? 5 - answer.RawValue : answer.RawValue)))
                .ToDictionary(group => group.Key, group => group.Count());
            Assert.Equal(["flourishing", "transitional", "vulnerable"], tiers.Keys.Order(StringComparer.Ordinal));
            Assert.All(tiers.Values, count => Assert.True(count >= 10, $"{count}"));
            Assert.All(completed, household => Assert.Equal(39, wave(household)!.Answers.Count));

            // The members who answer: the head, or in wave two the new head of the household whose head changed.
            var answering = completed.Select(household => household.Members[AnsweringIndex(household, wave(household)!)]).ToList();
            Assert.All(answering.GroupBy(AgeBand), group => Assert.True(group.Count() >= 10, group.Key));
            Assert.All(answering.GroupBy(member => member.Employment), group => Assert.True(group.Count() >= 10, $"{group.Key}"));
        }
    }

    private static int AnsweringIndex(DemonstrationHousehold household, DemonstrationSitting sitting) =>
        household.HeadChange is { } change && ReferenceEquals(sitting, household.WaveTwo) ? change.NewHeadIndex : 0;

    private static string AgeBand(DemonstrationMember member) => member.Age switch
    {
        < 30 => "18_29",
        < 45 => "30_44",
        < 60 => "45_59",
        _ => "60_plus",
    };
}
