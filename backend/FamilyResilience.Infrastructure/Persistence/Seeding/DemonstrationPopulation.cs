using System.Text.Json;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Infrastructure.Content;

namespace FamilyResilience.Infrastructure.Persistence.Seeding;

/// <summary>
/// The demonstration's synthetic population, generated from a fixed seed so every build is the same apart from the
/// dates, which follow the day it is built. Sixty nameless households: the household-lead persona's (number 1), one
/// whose head changes during wave two (2, the only cancelled assignment), four that join during wave two (54–57), one
/// that has not confirmed its roster (58) and two that withdrew (59 after answering wave one, 60 after letting it
/// expire). Households 7, 20, 33 and 43 gave no research-use consent. Only heads answer (the demonstration's
/// eligibility rule), so heads are spread over three age bands and three employment statuses with at least ten people
/// in each per wave (analytics hide groups of fewer than ten, D-58).
/// <para>
/// Answers come from a per-household tendency, applied in the direction each item is worded in the content file, aimed
/// well inside one of the approved rule set's tiers. Which tier is decided by rank among the research-consented results
/// of each wave, so each tier holds at least ten people in each wave and the shares move modestly between the waves.
/// The platform's scoring engine computes every stored total; nothing here is a score.
/// </para>
/// </summary>
internal static class DemonstrationPopulation
{
    public const int Seed = 140;
    public const int HouseholdCount = 60;
    public const int MinimumPeoplePerTier = 10;

    private const int Lead = 1;
    private const int HeadChange = 2;
    private const int Unconfirmed = 58;
    private const int WithdrawnAfterAnswering = 59;
    private const int WithdrawnAfterExpiry = 60;
    private const int TierMargin = 6;

    private static readonly int[] LateJoiners = [54, 55, 56, 57];
    private static readonly int[] WithoutResearchUse = [7, 20, 33, 43];
    private static readonly int[] WaveOneNotStarted = [10, 22, 34, 46, 51, WithdrawnAfterExpiry];
    private static readonly int[] WaveOneAbandoned = [15, 39];
    private static readonly int[] WaveTwoInProgress = [5, 13, 21, 29, 37, 43, 50, 56];
    private static readonly int[] WaveTwoNotStarted = [Lead, 8, 16, 24, 32, 40, 48, 53, 57];

    // Shares of the research-consented results in the first two tiers; the last tier takes the rest.
    private static readonly double[] WaveOneShares = [0.33, 0.41];
    private static readonly double[] WaveTwoShares = [0.30, 0.38];

    public static DemonstrationPlan Build(DateTimeOffset now)
    {
        var timeline = DemonstrationTimeline.At(now);
        var random = new Random(Seed);
        var items = Items();
        var tiers = TierBands();
        var tendency = new double[HouseholdCount + 1];
        var drift = new double[HouseholdCount + 1];
        for (var number = 1; number <= HouseholdCount; number++)
        {
            tendency[number] = random.NextDouble();
            drift[number] = (random.NextDouble() * 0.2) - 0.08;
        }

        var numbers = Enumerable.Range(1, HouseholdCount).ToList();
        var waveOneTiers = TiersByRank(
            numbers.Where(number => InWaveOne(number) && WaveOneOutcome(number) == DemonstrationOutcome.Completed
                && ResearchUse(number) && !Withdraws(number)),
            number => tendency[number], WaveOneShares, tiers.Count);
        var waveTwoTiers = TiersByRank(
            numbers.Where(number => InWaveTwo(number) && WaveTwoOutcome(number) == DemonstrationOutcome.Completed && ResearchUse(number)),
            number => tendency[number] + drift[number], WaveTwoShares, tiers.Count);

        int TierOf(int number, IReadOnlyDictionary<int, int> ranked, double value) =>
            ranked.TryGetValue(number, out var tier) ? tier : Math.Clamp((int)(value * tiers.Count), 0, tiers.Count - 1);

        var nonSeniorRank = 0;
        var households = new List<DemonstrationHousehold>();
        foreach (var number in numbers)
        {
            var ageBand = (number - 1) % 3;
            var headEmployment = ageBand == 2
                ? EmploymentStatus.Retired
                : nonSeniorRank++ % 9 is 2 or 4 or 6 or 8 ? EmploymentStatus.SelfEmployed : EmploymentStatus.Employed;
            var members = Roster(number, ageBand, headEmployment, random);
            var createdAt = CreatedAt(number, timeline);
            var confirmedAt = number == Unconfirmed
                ? (DateTimeOffset?)null
                : InWaveOne(number) ? createdAt.AddHours(1) : createdAt.AddMinutes(40);
            var headChange = number == HeadChange
                ? new DemonstrationHeadChange(1, HouseholdRelationship.Spouse, timeline.WaveTwoOpens.AddDays(4).AddHours(10))
                : null;

            DemonstrationSitting? waveOne = null;
            if (InWaveOne(number))
            {
                var outcome = WaveOneOutcome(number);
                var values = CalculatedValues(tiers[TierOf(number, waveOneTiers, tendency[number])], items.Count, random);
                var start = outcome switch
                {
                    DemonstrationOutcome.Completed => DuringTheDay(
                        timeline.WaveOneOpens.AddDays(1).AddDays(86 * Math.Pow(random.NextDouble(), 1.8)),
                        timeline.WaveOneOpens.AddDays(1), timeline.WaveOneCloses.AddDays(-1), random),
                    DemonstrationOutcome.Abandoned => DuringTheDay(
                        timeline.WaveOneOpens.AddDays(10).AddDays(60 * random.NextDouble()),
                        timeline.WaveOneOpens.AddDays(1), timeline.WaveOneCloses.AddDays(-1), random),
                    _ => (DateTimeOffset?)null,
                };
                waveOne = Sitting(outcome, timeline.WaveOneOpens.AddMinutes(number), start, values, items, random);
            }

            DemonstrationSitting? waveTwo = null;
            if (InWaveTwo(number))
            {
                var outcome = WaveTwoOutcome(number);
                var values = CalculatedValues(
                    tiers[TierOf(number, waveTwoTiers, tendency[number] + drift[number])], items.Count, random);
                var assignedAt = InWaveOne(number) ? timeline.WaveTwoOpens.AddMinutes(number) : confirmedAt!.Value;
                var earliest = headChange is not null
                    ? headChange.At.AddDays(1)
                    : Later(assignedAt.AddHours(6), timeline.WaveTwoOpens.AddDays(1));
                var latest = timeline.Now.AddDays(-1);
                var start = outcome is DemonstrationOutcome.Completed or DemonstrationOutcome.InProgress
                    ? DuringTheDay(earliest + ((latest - earliest) * Math.Pow(random.NextDouble(), 1.5)), earliest, latest, random)
                    : (DateTimeOffset?)null;
                waveTwo = Sitting(outcome, assignedAt, start, values, items, random);
            }

            households.Add(new DemonstrationHousehold(
                number,
                Reference(number, random),
                createdAt,
                confirmedAt,
                members,
                number == Unconfirmed ? Array.Empty<int>() : number == HeadChange ? new[] { 0, 1 } : new[] { 0 },
                ResearchUse(number),
                waveOne,
                waveTwo,
                headChange,
                number switch
                {
                    WithdrawnAfterAnswering => timeline.Now.AddDays(-100),
                    WithdrawnAfterExpiry => timeline.Now.AddDays(-95),
                    _ => (DateTimeOffset?)null,
                },
                number == Lead));
        }

        return new DemonstrationPlan(timeline, households);
    }

    private static bool InWaveOne(int number) => number is < 54 or WithdrawnAfterAnswering or WithdrawnAfterExpiry;

    private static bool InWaveTwo(int number) => number <= LateJoiners[^1];

    private static bool Withdraws(int number) => number is WithdrawnAfterAnswering or WithdrawnAfterExpiry;

    private static bool ResearchUse(int number) => number != Unconfirmed && !WithoutResearchUse.Contains(number);

    private static DemonstrationOutcome WaveOneOutcome(int number) =>
        WaveOneNotStarted.Contains(number) ? DemonstrationOutcome.NotStarted
        : WaveOneAbandoned.Contains(number) ? DemonstrationOutcome.Abandoned
        : DemonstrationOutcome.Completed;

    private static DemonstrationOutcome WaveTwoOutcome(int number) =>
        WaveTwoInProgress.Contains(number) ? DemonstrationOutcome.InProgress
        : WaveTwoNotStarted.Contains(number) ? DemonstrationOutcome.NotStarted
        : DemonstrationOutcome.Completed;

    private static DateTimeOffset CreatedAt(int number, DemonstrationTimeline timeline)
    {
        var late = Array.IndexOf(LateJoiners, number);
        return late >= 0 ? timeline.WaveTwoOpens.AddDays(2 + (2 * late)).AddHours(9)
            : number == Unconfirmed ? timeline.Now.AddDays(-3)
            : timeline.WaveOneOpens.AddDays(-20).AddHours(5 * (number - 1));
    }

    /// <summary>
    /// Ranks the households by tendency and gives the lowest the first tier, the next the second and the rest the
    /// last, in the given shares. Each tier must hold at least <see cref="MinimumPeoplePerTier"/> people.
    /// </summary>
    private static Dictionary<int, int> TiersByRank(IEnumerable<int> numbers, Func<int, double> value, double[] shares, int tierCount)
    {
        var ranked = numbers.OrderBy(value).ThenBy(number => number).ToList();
        var sizes = shares.Select(share => (int)Math.Round(ranked.Count * share, MidpointRounding.AwayFromZero)).ToList();
        sizes.Add(ranked.Count - sizes.Sum());
        if (sizes.Count != tierCount || sizes.Any(size => size < MinimumPeoplePerTier))
        {
            throw new InvalidOperationException("The demonstration plan must give every tier at least ten people in each wave.");
        }

        var tiers = new Dictionary<int, int>();
        var position = 0;
        for (var tier = 0; tier < sizes.Count; tier++)
        {
            foreach (var number in ranked.Skip(position).Take(sizes[tier]))
            {
                tiers[number] = tier;
            }

            position += sizes[tier];
        }

        return tiers;
    }

    private static DemonstrationSitting Sitting(
        DemonstrationOutcome outcome,
        DateTimeOffset assignedAt,
        DateTimeOffset? startedAt,
        int[] values,
        IReadOnlyList<PlannedItem> items,
        Random random)
    {
        var count = outcome switch
        {
            DemonstrationOutcome.Completed => items.Count,
            DemonstrationOutcome.InProgress => random.Next(5, 32),
            DemonstrationOutcome.Abandoned => random.Next(6, 25),
            _ => 0,
        };
        var answers = new List<DemonstrationAnswer>();
        var at = startedAt ?? assignedAt;
        for (var index = 0; index < count; index++)
        {
            at = at.AddSeconds(random.Next(15, 46));
            var item = items[index];
            answers.Add(new DemonstrationAnswer(item.Code, item.Reverse ? item.Mirror - values[index] : values[index], at));
        }

        var submittedAt = outcome == DemonstrationOutcome.Completed ? at.AddSeconds(random.Next(30, 121)) : (DateTimeOffset?)null;
        return new DemonstrationSitting(outcome, assignedAt, startedAt, answers, submittedAt);
    }

    /// <summary>
    /// Values on the scale's own terms (1 the least resilient answer, 4 the most) that add up to a total chosen inside
    /// <paramref name="tier"/>, away from its edges. A reverse-worded item then takes the mirrored option.
    /// </summary>
    private static int[] CalculatedValues((int Min, int Max) tier, int count, Random random)
    {
        var total = random.Next(tier.Min + TierMargin, tier.Max - TierMargin + 1);
        var mean = (double)total / count;
        var values = new int[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = Math.Clamp((int)Math.Round(mean + random.NextDouble() - 0.5, MidpointRounding.AwayFromZero), 1, 4);
        }

        var sum = values.Sum();
        while (sum != total)
        {
            var index = random.Next(count);
            if (sum < total && values[index] < 4)
            {
                values[index]++;
                sum++;
            }
            else if (sum > total && values[index] > 1)
            {
                values[index]--;
                sum--;
            }
        }

        return values;
    }

    private static List<DemonstrationMember> Roster(int number, int ageBand, EmploymentStatus headEmployment, Random random)
    {
        if (number == Lead)
        {
            // As the mobile development fixtures' household lead: an employed head of 42, a spouse at home and a child.
            return
            [
                new(42, 3, null, EmploymentStatus.Employed, IsHead: true, IsPrimaryEarner: true),
                new(39, 9, HouseholdRelationship.Spouse, EmploymentStatus.Homemaker, IsHead: false, IsPrimaryEarner: false),
                new(12, 5, HouseholdRelationship.Child, null, IsHead: false, IsPrimaryEarner: false),
            ];
        }

        if (number == HeadChange)
        {
            return
            [
                new(50, 6, null, EmploymentStatus.Employed, IsHead: true, IsPrimaryEarner: true),
                new(46, 2, HouseholdRelationship.Spouse, EmploymentStatus.Employed, IsHead: false, IsPrimaryEarner: false),
                new(21, 10, HouseholdRelationship.Child, EmploymentStatus.Student, IsHead: false, IsPrimaryEarner: false),
                new(14, 4, HouseholdRelationship.Child, null, IsHead: false, IsPrimaryEarner: false),
            ];
        }

        // Ages stay a year or more inside their band, so the overview's bands hold for months after the build.
        var headAge = ageBand switch
        {
            0 => random.Next(31, 44),
            1 => random.Next(46, 59),
            _ => random.Next(61, 75),
        };
        var others = new List<(int Age, HouseholdRelationship Relationship, EmploymentStatus? Employment)>();
        if (random.NextDouble() < (ageBand == 2 ? 0.7 : 0.85))
        {
            var spouseAge = Math.Max(22, headAge + random.Next(-7, 4));
            others.Add((spouseAge, HouseholdRelationship.Spouse, spouseAge >= 60
                ? random.NextDouble() < 0.7 ? EmploymentStatus.Retired : EmploymentStatus.Homemaker
                : Pick(random, (EmploymentStatus.Homemaker, 35), (EmploymentStatus.Employed, 35), (EmploymentStatus.SelfEmployed, 10),
                    (EmploymentStatus.Unemployed, 10), (EmploymentStatus.Other, 5), (EmploymentStatus.Student, 5))));
        }

        var children = ageBand == 2 ? Pick(random, (0, 2), (1, 2), (2, 1)) : Pick(random, (0, 1), (1, 2), (2, 3), (3, 2), (4, 1));
        for (var child = 0; child < children; child++)
        {
            // Never 17 to 19: an adult needs an employment status from the day the roster is confirmed, months ago.
            var age = ageBand == 2 ? headAge - random.Next(24, 40) : random.Next(2, Math.Max(3, headAge - 21));
            age = age is >= 17 and <= 19 ? (random.NextDouble() < 0.5 ? 16 : 20) : age;
            others.Add((age, HouseholdRelationship.Child, age < 18 ? (EmploymentStatus?)null
                : Pick(random, (age <= 26 ? EmploymentStatus.Student : EmploymentStatus.Employed, 35), (EmploymentStatus.Employed, 40),
                    (EmploymentStatus.Unemployed, 15), (EmploymentStatus.SelfEmployed, 5), (EmploymentStatus.Other, 5))));
        }

        if (ageBand == 0 && random.NextDouble() < 0.3)
        {
            others.Add((random.Next(64, 86), HouseholdRelationship.Parent,
                random.NextDouble() < 0.7 ? EmploymentStatus.Retired : EmploymentStatus.Homemaker));
        }

        if (ageBand == 2 && random.NextDouble() < 0.3)
        {
            others.Add((random.Next(2, 16), HouseholdRelationship.Grandchild, null));
        }

        // A retired head usually lives on a pension; an earning adult child, when there is one, earns the most.
        var earner = headEmployment == EmploymentStatus.Retired
            ? others.FindIndex(other => other.Relationship == HouseholdRelationship.Child
                && other.Employment is EmploymentStatus.Employed or EmploymentStatus.SelfEmployed)
            : -1;
        var roster = new List<DemonstrationMember>
        {
            new(headAge, random.Next(1, 13), null, headEmployment, IsHead: true, IsPrimaryEarner: earner < 0),
        };
        roster.AddRange(others.Select((other, index) =>
            new DemonstrationMember(other.Age, random.Next(1, 13), other.Relationship, other.Employment, IsHead: false, IsPrimaryEarner: index == earner)));
        return roster;
    }

    private static T Pick<T>(Random random, params (T Value, int Weight)[] choices)
    {
        var roll = random.Next(choices.Sum(choice => choice.Weight));
        foreach (var (value, weight) in choices)
        {
            if (roll < weight)
            {
                return value;
            }

            roll -= weight;
        }

        return choices[^1].Value;
    }

    /// <summary>A recognisable demonstration reference such as <c>HH-DM07-K3QX</c> (Crockford base 32, like every reference).</summary>
    private static string Reference(int number, Random random)
    {
        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var tail = string.Concat(Enumerable.Range(0, 4).Select(_ => Alphabet[random.Next(Alphabet.Length)]));
        return $"HH-DM{number:00}-{tail}";
    }

    /// <summary>
    /// A time between 08:00 and 21:00 in the UAE (04:00 to 17:00 UTC) on the day of <paramref name="candidate"/>, within
    /// bounds. It always draws the same numbers, whatever the dates, so the plan is the same on every day it is built.
    /// </summary>
    private static DateTimeOffset DuringTheDay(DateTimeOffset candidate, DateTimeOffset earliest, DateTimeOffset latest, Random random)
    {
        var minutes = random.Next(0, 13 * 60);
        var seconds = random.Next(0, 60);
        var fallback = random.Next(5, 60);
        var time = new DateTimeOffset(candidate.UtcDateTime.Date, TimeSpan.Zero).AddHours(4).AddMinutes(minutes).AddSeconds(seconds);
        if (time < earliest)
        {
            time = earliest.AddMinutes(fallback);
        }

        return time > latest ? latest : time;
    }

    private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second) => first > second ? first : second;

    /// <summary>The household assessment's items in display order, with their wording direction and scale, from the content file.</summary>
    private static List<PlannedItem> Items()
    {
        var package = ContentFiles.ReadPackage(ContentFiles.HouseholdAssessment);
        return package.Version.Sections
            .OrderBy(section => section.DisplayOrder)
            .SelectMany(section => section.Items.OrderBy(item => item.DisplayOrder))
            .Select(item =>
            {
                var values = item.Options.Where(option => !option.IsNonSubstantive).Select(option => option.Value).ToList();
                return new PlannedItem(item.Code, item.ScoringDirection == ScoringDirection.Reverse, values.Min() + values.Max());
            })
            .ToList();
    }

    /// <summary>The tier bands of the scoring rule set the demonstration approves, in order, from its content file.</summary>
    private static List<(int Min, int Max)> TierBands()
    {
        using var document = JsonDocument.Parse(ContentFiles.Read(ContentFiles.HouseholdAssessmentScoring));
        return document.RootElement.GetProperty("parameters").GetProperty("tierBands").EnumerateArray()
            .Select(band => (band.GetProperty("min").GetInt32(), band.GetProperty("max").GetInt32()))
            .OrderBy(band => band.Item1)
            .ToList();
    }

    private sealed record PlannedItem(string Code, bool Reverse, int Mirror);
}
