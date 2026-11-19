using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// The household assessment's submitted check-ins for the trend chart (D-132). The newest sessions are read first
/// (without their answers), then their waves and latest results, all untracked.
/// </summary>
public sealed class ResilienceTrendReadModel(FamilyResilienceDbContext dbContext) : IResilienceTrendReadModel
{
    public async Task<IReadOnlyList<ResilienceCheckIn>> ListCheckInsAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int take, CancellationToken cancellationToken = default)
    {
        var householdVersions = dbContext.InstrumentVersions.AsNoTracking()
            .Join(dbContext.Instruments.Where(instrument => instrument.Kind == InstrumentKind.HouseholdAssessment),
                version => version.InstrumentId, instrument => instrument.Id, (version, _) => version.Id);
        var sessions = await dbContext.AssessmentSessions.AsNoTracking()
            .Where(session => memberIds.Contains(session.FamilyMemberId)
                && (session.Status == SessionStatus.Submitted || session.Status == SessionStatus.Scored)
                && session.SubmittedAt != null
                && householdVersions.Contains(session.InstrumentVersionId))
            .OrderByDescending(session => session.SubmittedAt)
            .ThenByDescending(session => session.Id)
            .Take(take)
            .Select(session => new { session.Id, session.AssessmentAssignmentId, SubmittedAt = session.SubmittedAt!.Value })
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            return [];
        }

        var assignmentIds = sessions.Select(session => session.AssessmentAssignmentId).ToList();
        var waveByAssignment = await dbContext.AssessmentAssignments.AsNoTracking()
            .Where(assignment => assignmentIds.Contains(assignment.Id))
            .Join(dbContext.Waves, assignment => assignment.WaveId, wave => wave.Id, (assignment, wave) => new { assignment.Id, wave.Code })
            .ToDictionaryAsync(pair => pair.Id, pair => pair.Code, cancellationToken);
        var sessionIds = sessions.Select(session => session.Id).ToList();
        var results = await dbContext.ScoreResults.AsNoTracking()
            .Where(result => sessionIds.Contains(result.AssessmentSessionId))
            .Select(result => new { result.Id, result.AssessmentSessionId, result.Status, result.TotalScore, result.TierCode, result.RuleSetId, result.ComputedAt })
            .ToListAsync(cancellationToken);
        // One result per session: the last computed, and of two computed at the same instant the one with the greater
        // identifier (version 7, so the one recorded later), as the household results choose it (D-139).
        var latest = results
            .GroupBy(result => result.AssessmentSessionId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(result => result.ComputedAt).ThenByDescending(result => result.Id.Value).First());

        return sessions.Select(session =>
        {
            var result = latest.GetValueOrDefault(session.Id);
            return new ResilienceCheckIn(
                session.Id.Value,
                waveByAssignment[session.AssessmentAssignmentId],
                session.SubmittedAt,
                result?.Status,
                result?.TotalScore,
                result?.TierCode,
                result?.RuleSetId);
        }).ToList();
    }
}
