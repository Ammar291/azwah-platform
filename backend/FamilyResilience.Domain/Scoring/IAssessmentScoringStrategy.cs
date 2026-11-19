using System.Text.Json;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// One deterministic scoring algorithm. A scoring rule set names it by <see cref="PolicyCode"/> and
/// <see cref="AlgorithmVersion"/>; the parameters come from the approved rule set. Implementations are pure.
/// </summary>
public interface IAssessmentScoringStrategy
{
    string PolicyCode { get; }

    int AlgorithmVersion { get; }

    Result<ScoringParameters> ValidateParameters(JsonElement parameters, ScoringContent content);

    Result<ScoringComputation> Score(ScoringInput input);
}
