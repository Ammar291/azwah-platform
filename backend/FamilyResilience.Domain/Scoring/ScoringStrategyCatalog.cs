using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

/// <summary>The strategies this engine supports. A rule set naming anything else is an unsupported scoring policy.</summary>
public sealed class ScoringStrategyCatalog
{
    private readonly List<IAssessmentScoringStrategy> _strategies;

    public ScoringStrategyCatalog(IEnumerable<IAssessmentScoringStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies.ToList();
        if (_strategies.GroupBy(strategy => (strategy.PolicyCode, strategy.AlgorithmVersion)).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Each policy code and algorithm version may be registered once.", nameof(strategies));
        }
    }

    public Result<IAssessmentScoringStrategy> Resolve(string policyCode, int algorithmVersion) =>
        _strategies.Find(strategy => strategy.PolicyCode == policyCode && strategy.AlgorithmVersion == algorithmVersion) is { } found
            ? Result.Success(found)
            : Result.Failure<IAssessmentScoringStrategy>(ScoringErrors.PolicyUnsupported);
}
