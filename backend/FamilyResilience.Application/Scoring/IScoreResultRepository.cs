using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Scoring;

public interface IScoreResultRepository
{
    void Add(ScoreResult result);

    /// <summary>The result of one rule set for one session, with its contributions and category scores, if any.</summary>
    Task<ScoreResult?> GetForSessionAndRuleSetAsync(
        AssessmentSessionId sessionId, RuleSetId ruleSetId, CancellationToken cancellationToken = default);

    /// <summary>Every result for the session, oldest first, with contributions and category scores.</summary>
    Task<IReadOnlyList<ScoreResult>> ListForSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken = default);
}
