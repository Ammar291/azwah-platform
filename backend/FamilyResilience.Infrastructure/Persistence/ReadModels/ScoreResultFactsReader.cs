using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Scoring;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// A score result's facts in one query: the producing scoring rule set's code, the status and tier, the session, and the
/// session's assignment member with that member's household. Nothing is tracked, and no total or answer is read.
/// </summary>
public sealed class ScoreResultFactsReader(FamilyResilienceDbContext dbContext) : IScoreResultFacts
{
    public Task<ScoreResultFacts?> GetAsync(ScoreResultId id, CancellationToken cancellationToken = default) =>
        (from result in dbContext.ScoreResults.AsNoTracking()
         join ruleSet in dbContext.RuleSets on result.RuleSetId equals ruleSet.Id
         join session in dbContext.AssessmentSessions on result.AssessmentSessionId equals session.Id
         join assignment in dbContext.AssessmentAssignments on session.AssessmentAssignmentId equals assignment.Id
         join member in dbContext.Set<FamilyMember>() on assignment.FamilyMemberId equals member.Id
         where result.Id == id
         select new ScoreResultFacts(
             ruleSet.Code, result.Status, result.TierCode, result.AssessmentSessionId, member.HouseholdId, assignment.FamilyMemberId))
        .SingleOrDefaultAsync(cancellationToken);
}
