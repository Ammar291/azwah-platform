using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Scoring;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// The household results list (D-104): each session's latest score result with its household, wave and questionnaire.
/// Filters, counts and paging run in PostgreSQL; the page's section codes are read in one query. No answers or members.
/// </summary>
public sealed class HouseholdResultReadModel(FamilyResilienceDbContext dbContext) : IHouseholdResultReadModel
{
    public async Task<PagedResponse<HouseholdResultRowResponse>> ListLatestAsync(
        HouseholdResultsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Exactly one result per session: the last computed, and of two computed at the same instant the one with the
        // greater identifier (version 7, so the one recorded later).
        var latest = dbContext.ScoreResults.AsNoTracking().Where(result => result.Id == dbContext.ScoreResults
            .Where(candidate => candidate.AssessmentSessionId == result.AssessmentSessionId)
            .OrderByDescending(candidate => candidate.ComputedAt)
            .ThenByDescending(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .First());
        var query =
            from result in latest
            join session in dbContext.AssessmentSessions on result.AssessmentSessionId equals session.Id
            join assignment in dbContext.AssessmentAssignments on session.AssessmentAssignmentId equals assignment.Id
            join member in dbContext.Set<FamilyMember>() on assignment.FamilyMemberId equals member.Id
            join household in dbContext.Households on member.HouseholdId equals household.Id
            join wave in dbContext.Waves on assignment.WaveId equals wave.Id
            join version in dbContext.InstrumentVersions on result.InstrumentVersionId equals version.Id
            join instrument in dbContext.Instruments on version.InstrumentId equals instrument.Id
            select new
            {
                result.Id,
                result.AssessmentSessionId,
                HouseholdId = household.Id,
                household.Reference,
                assignment.WaveId,
                WaveCode = wave.Code,
                InstrumentCode = instrument.Code,
                version.VersionNumber,
                assignment.InstrumentSectionId,
                result.Status,
                result.TotalScore,
                result.TierCode,
                result.ComputedAt,
            };
        if (request.WaveId is { } waveId)
        {
            var wave = WaveId.From(waveId);
            query = query.Where(row => row.WaveId == wave);
        }

        if (request.InstrumentCode is { } code)
        {
            query = query.Where(row => row.InstrumentCode == code);
        }

        if (request.Reference is { } text && HouseholdReference.Parse(text) is { IsSuccess: true } reference)
        {
            query = query.Where(row => row.Reference == reference.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await query
            .OrderByDescending(row => row.ComputedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var sectionIds = page.Select(row => row.InstrumentSectionId).OfType<InstrumentSectionId>().Distinct().ToList();
        var sectionCodes = await dbContext.Set<InstrumentSection>().AsNoTracking()
            .Where(section => sectionIds.Contains(section.Id))
            .ToDictionaryAsync(section => section.Id, section => section.Code, cancellationToken);

        return new PagedResponse<HouseholdResultRowResponse>(
            page.Select(row => new HouseholdResultRowResponse(
                row.AssessmentSessionId.Value,
                row.HouseholdId.Value,
                row.Reference.Value,
                row.WaveCode,
                row.InstrumentCode,
                row.VersionNumber,
                row.InstrumentSectionId is { } sectionId ? sectionCodes[sectionId] : null,
                row.Status,
                row.TotalScore,
                row.TierCode,
                row.ComputedAt)).ToList(),
            request.Page,
            request.PageSize,
            total);
    }
}
