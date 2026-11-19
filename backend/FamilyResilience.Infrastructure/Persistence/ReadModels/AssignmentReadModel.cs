using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Participant assignment views. One page of assignments is read first, then the few versions, instruments, sections,
/// waves and live sessions it names, all untracked.
/// </summary>
public sealed class AssignmentReadModel(FamilyResilienceDbContext dbContext) : IAssignmentReadModel
{
    public async Task<IReadOnlyList<ParticipantAssignmentResponse>> ListAsync(
        IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var page = await Filter(memberIds, status)
            .OrderByDescending(assignment => assignment.AssignedAt)
            .ThenByDescending(assignment => assignment.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return await ProjectAsync(page, cancellationToken);
    }

    public Task<int> CountAsync(IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status, CancellationToken cancellationToken = default) =>
        Filter(memberIds, status).CountAsync(cancellationToken);

    public async Task<ParticipantAssignmentResponse?> GetAsync(AssessmentAssignmentId id, CancellationToken cancellationToken = default)
    {
        var found = await dbContext.AssessmentAssignments.AsNoTracking()
            .Where(assignment => assignment.Id == id)
            .ToListAsync(cancellationToken);
        return (await ProjectAsync(found, cancellationToken)).SingleOrDefault();
    }

    private IQueryable<AssessmentAssignment> Filter(IReadOnlyCollection<FamilyMemberId> memberIds, AssignmentStatus? status)
    {
        var query = dbContext.AssessmentAssignments.AsNoTracking().Where(assignment => memberIds.Contains(assignment.FamilyMemberId));
        return status is { } wanted ? query.Where(assignment => assignment.Status == wanted) : query;
    }

    private async Task<IReadOnlyList<ParticipantAssignmentResponse>> ProjectAsync(
        IReadOnlyList<AssessmentAssignment> assignments, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0)
        {
            return [];
        }

        var versionIds = assignments.Select(assignment => assignment.InstrumentVersionId).Distinct().ToList();
        var versions = await dbContext.InstrumentVersions.AsNoTracking()
            .Where(version => versionIds.Contains(version.Id))
            .Select(version => new { version.Id, version.VersionNumber, version.InstrumentId })
            .ToDictionaryAsync(version => version.Id, cancellationToken);
        var instrumentIds = versions.Values.Select(version => version.InstrumentId).Distinct().ToList();
        var instruments = await dbContext.Instruments.AsNoTracking()
            .Where(instrument => instrumentIds.Contains(instrument.Id))
            .ToDictionaryAsync(instrument => instrument.Id, cancellationToken);
        var sectionIds = assignments.Select(assignment => assignment.InstrumentSectionId).OfType<InstrumentSectionId>().Distinct().ToList();
        var sections = await dbContext.Set<InstrumentSection>().AsNoTracking()
            .Where(section => sectionIds.Contains(section.Id))
            .ToDictionaryAsync(section => section.Id, cancellationToken);
        var waveIds = assignments.Select(assignment => assignment.WaveId).Distinct().ToList();
        var waves = await dbContext.Waves.AsNoTracking()
            .Where(wave => waveIds.Contains(wave.Id))
            .ToDictionaryAsync(wave => wave.Id, cancellationToken);
        var assignmentIds = assignments.Select(assignment => assignment.Id).ToList();
        var liveSessions = await dbContext.AssessmentSessions.AsNoTracking()
            .Where(session => assignmentIds.Contains(session.AssessmentAssignmentId) && session.Status != SessionStatus.Abandoned)
            .Select(session => new { session.AssessmentAssignmentId, session.Id })
            .ToDictionaryAsync(session => session.AssessmentAssignmentId, session => session.Id, cancellationToken);

        return assignments.Select(assignment =>
        {
            var version = versions[assignment.InstrumentVersionId];
            var instrument = instruments[version.InstrumentId];
            var section = assignment.InstrumentSectionId is { } sectionId ? sections[sectionId] : null;
            return new ParticipantAssignmentResponse(
                assignment.Id.Value,
                assignment.FamilyMemberId.Value,
                instrument.Code,
                new TextDto(instrument.Name.Arabic, instrument.Name.English),
                version.VersionNumber,
                section?.Code,
                section is null ? null : new TextDto(section.Title.Arabic, section.Title.English),
                assignment.WaveId.Value,
                waves[assignment.WaveId].Code,
                assignment.Status,
                assignment.AssignedAt,
                assignment.ExpiresAt,
                assignment.CompletedAt,
                liveSessions.TryGetValue(assignment.Id, out var sessionId) ? sessionId.Value : null,
                instrument.Kind);
        }).ToList();
    }
}
