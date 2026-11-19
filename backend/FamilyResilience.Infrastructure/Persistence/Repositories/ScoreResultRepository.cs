using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.Repositories;

public sealed class ScoreResultRepository(FamilyResilienceDbContext dbContext) : IScoreResultRepository
{
    public void Add(ScoreResult result) => dbContext.ScoreResults.Add(result);

    public Task<ScoreResult?> GetForSessionAndRuleSetAsync(
        AssessmentSessionId sessionId, RuleSetId ruleSetId, CancellationToken cancellationToken = default) =>
        WithDetails().SingleOrDefaultAsync(
            result => result.AssessmentSessionId == sessionId && result.RuleSetId == ruleSetId, cancellationToken);

    public async Task<IReadOnlyList<ScoreResult>> ListForSessionAsync(
        AssessmentSessionId sessionId, CancellationToken cancellationToken = default) =>
        await WithDetails()
            .Where(result => result.AssessmentSessionId == sessionId)
            .OrderBy(result => result.ComputedAt)
            .ToListAsync(cancellationToken);

    private IQueryable<ScoreResult> WithDetails() =>
        dbContext.ScoreResults
            .Include(result => result.Contributions)
            .Include(result => result.CategoryScores)
            .AsSplitQuery();
}
