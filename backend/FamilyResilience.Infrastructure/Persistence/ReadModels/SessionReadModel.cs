using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Assessment history. One page of sessions is read first (without their answers), then the few assignments,
/// versions, instruments, sections, waves and latest results it names, all untracked.
/// </summary>
public sealed class SessionReadModel(FamilyResilienceDbContext dbContext) : ISessionReadModel
{
    public async Task<IReadOnlyList<AssessmentHistoryItemResponse>> ListHistoryAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, int skip, int take, CancellationToken cancellationToken = default)
    {
        var page = await History(memberIds)
            .OrderByDescending(session => session.SubmittedAt)
            .ThenByDescending(session => session.Id)
            .Skip(skip)
            .Take(take)
            .Select(session => new
            {
                session.Id,
                session.AssessmentAssignmentId,
                session.FamilyMemberId,
                session.InstrumentVersionId,
                session.InstrumentSectionId,
                session.Status,
                session.StartedAt,
                session.SubmittedAt,
            })
            .ToListAsync(cancellationToken);
        if (page.Count == 0)
        {
            return [];
        }

        var assignmentIds = page.Select(session => session.AssessmentAssignmentId).ToList();
        var waveByAssignment = await dbContext.AssessmentAssignments.AsNoTracking()
            .Where(assignment => assignmentIds.Contains(assignment.Id))
            .Join(dbContext.Waves, assignment => assignment.WaveId, wave => wave.Id, (assignment, wave) => new { assignment.Id, wave.Code })
            .ToDictionaryAsync(pair => pair.Id, pair => pair.Code, cancellationToken);
        var versionIds = page.Select(session => session.InstrumentVersionId).Distinct().ToList();
        var versions = await dbContext.InstrumentVersions.AsNoTracking()
            .Where(version => versionIds.Contains(version.Id))
            .Join(dbContext.Instruments, version => version.InstrumentId, instrument => instrument.Id,
                (version, instrument) => new { version.Id, version.VersionNumber, instrument.Code })
            .ToDictionaryAsync(version => version.Id, cancellationToken);
        var sectionIds = page.Select(session => session.InstrumentSectionId).OfType<InstrumentSectionId>().Distinct().ToList();
        var sectionCodes = await dbContext.Set<InstrumentSection>().AsNoTracking()
            .Where(section => sectionIds.Contains(section.Id))
            .ToDictionaryAsync(section => section.Id, section => section.Code, cancellationToken);
        var sessionIds = page.Select(session => session.Id).ToList();
        var results = await dbContext.ScoreResults.AsNoTracking()
            .Where(result => sessionIds.Contains(result.AssessmentSessionId))
            .Select(result => new { result.AssessmentSessionId, result.Status, result.TierCode, result.ComputedAt })
            .ToListAsync(cancellationToken);
        var latest = results
            .GroupBy(result => result.AssessmentSessionId)
            .ToDictionary(group => group.Key, group => group.MaxBy(result => result.ComputedAt)!);

        return page.Select(session =>
        {
            var version = versions[session.InstrumentVersionId];
            var result = latest.GetValueOrDefault(session.Id);
            return new AssessmentHistoryItemResponse(
                session.Id.Value,
                session.AssessmentAssignmentId.Value,
                session.FamilyMemberId.Value,
                version.Code,
                version.VersionNumber,
                session.InstrumentSectionId is { } sectionId ? sectionCodes[sectionId] : null,
                waveByAssignment[session.AssessmentAssignmentId],
                session.Status,
                session.StartedAt,
                session.SubmittedAt,
                result?.Status,
                result?.TierCode);
        }).ToList();
    }

    public Task<int> CountHistoryAsync(IReadOnlyCollection<FamilyMemberId> memberIds, CancellationToken cancellationToken = default) =>
        History(memberIds).CountAsync(cancellationToken);

    private IQueryable<AssessmentSession> History(IReadOnlyCollection<FamilyMemberId> memberIds) =>
        dbContext.AssessmentSessions.AsNoTracking()
            .Where(session => memberIds.Contains(session.FamilyMemberId)
                && (session.Status == SessionStatus.Submitted || session.Status == SessionStatus.Scored));
}
