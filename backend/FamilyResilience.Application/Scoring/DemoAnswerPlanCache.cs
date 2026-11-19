using System.Collections.Concurrent;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// DEMONSTRATIONS ONLY (D-143). The plans <see cref="DemoAnswerPlanner"/> has made, kept for the life of the API, so a
/// demonstration's tap does no planning after the first one for its rule set. An approved rule set and the version it
/// scores never change (D-25), so a plan made for one stays right for it. Keyed by the rule set, the scope and the level;
/// the planner looks up the approved rule set first on every call, so a plan for a rule set retired since is never used.
/// </summary>
public sealed class DemoAnswerPlanCache
{
    private readonly ConcurrentDictionary<(RuleSetId RuleSet, string? SectionCode, DemoScoreLevel Level), DemoAnswerPlan> _plans = new();

    /// <summary>How many plans are kept.</summary>
    public int Count => _plans.Count;

    public bool TryGet(RuleSetId ruleSet, string? sectionCode, DemoScoreLevel level, out DemoAnswerPlan plan)
    {
        var found = _plans.TryGetValue((ruleSet, sectionCode, level), out var kept);
        plan = kept!;
        return found;
    }

    public void Keep(RuleSetId ruleSet, string? sectionCode, DemoScoreLevel level, DemoAnswerPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plans[(ruleSet, sectionCode, level)] = plan;
    }
}
