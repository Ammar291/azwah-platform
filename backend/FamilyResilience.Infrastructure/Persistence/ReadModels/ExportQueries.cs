using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Untracked, fixed reads for research exports, as LINQ that EF Core translates to joins in PostgreSQL. Only members
/// whose research-use consent is active now, in households that have not withdrawn, are read; the wave and the
/// instrument code are the only filters.
/// </summary>
public sealed class ExportQueries(FamilyResilienceDbContext dbContext) : IExportQueries
{
    public async Task<IReadOnlyList<ExportSessionRow>> SessionsAsync(ExportFilter filter, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query =
            from session in dbContext.AssessmentSessions.AsNoTracking()
            where (session.Status == SessionStatus.Submitted || session.Status == SessionStatus.Scored)
                && ResearchConsented().Contains(session.FamilyMemberId)
            join assignment in dbContext.AssessmentAssignments on session.AssessmentAssignmentId equals assignment.Id
            join wave in dbContext.Waves on assignment.WaveId equals wave.Id
            join version in dbContext.InstrumentVersions on session.InstrumentVersionId equals version.Id
            join instrument in dbContext.Instruments on version.InstrumentId equals instrument.Id
            join member in dbContext.Set<FamilyMember>() on session.FamilyMemberId equals member.Id
            select new
            {
                session.Id,
                session.FamilyMemberId,
                member.HouseholdId,
                InstrumentCode = instrument.Code,
                version.VersionNumber,
                session.InstrumentSectionId,
                WaveId = wave.Id,
                WaveCode = wave.Code,
                session.Status,
                SubmittedAt = session.SubmittedAt!.Value,
                member.BirthMonth.Year,
                member.BirthMonth.Month,
                member.EmploymentStatus,
                member.IsHeadOfHousehold,
                AnsweredCount = session.Responses.Count,
                // Counted from the option the answer chose, as the version defines it; no answer leaves the database.
                NonSubstantiveCount = session.Responses.Count(response => dbContext.Set<ResponseOption>()
                    .Any(option => option.Id == response.ResponseOptionId && option.IsNonSubstantive)),
            };
        if (filter.WaveId is { } waveId)
        {
            query = query.Where(row => row.WaveId == waveId);
        }

        if (filter.InstrumentCode is { } instrumentCode)
        {
            query = query.Where(row => row.InstrumentCode == instrumentCode);
        }

        var rows = await query.OrderBy(row => row.Id).Take(limit).ToListAsync(cancellationToken);
        var sections = await SectionCodesAsync(rows.Select(row => row.InstrumentSectionId), cancellationToken);
        return rows
            .Select(row => new ExportSessionRow(
                row.FamilyMemberId, row.HouseholdId, row.InstrumentCode, row.VersionNumber,
                row.InstrumentSectionId is { } section ? sections[section] : null, row.WaveCode, row.Status, row.SubmittedAt,
                row.Year, row.Month, row.EmploymentStatus, row.IsHeadOfHousehold, row.AnsweredCount, row.NonSubstantiveCount))
            .ToList();
    }

    public async Task<IReadOnlyList<ExportScoreRow>> ScoresAsync(ExportFilter filter, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query =
            from result in dbContext.ScoreResults.AsNoTracking()
            join rules in dbContext.RuleSets on result.RuleSetId equals rules.Id
            where rules.Kind == RuleSetKind.Scoring && rules.Status == RuleSetStatus.Approved
            join session in dbContext.AssessmentSessions on result.AssessmentSessionId equals session.Id
            where ResearchConsented().Contains(session.FamilyMemberId)
            join assignment in dbContext.AssessmentAssignments on session.AssessmentAssignmentId equals assignment.Id
            join wave in dbContext.Waves on assignment.WaveId equals wave.Id
            join version in dbContext.InstrumentVersions on session.InstrumentVersionId equals version.Id
            join instrument in dbContext.Instruments on version.InstrumentId equals instrument.Id
            join member in dbContext.Set<FamilyMember>() on session.FamilyMemberId equals member.Id
            select new
            {
                result.Id,
                session.FamilyMemberId,
                member.HouseholdId,
                InstrumentCode = instrument.Code,
                version.VersionNumber,
                session.InstrumentSectionId,
                WaveId = wave.Id,
                WaveCode = wave.Code,
                RuleSetCode = rules.Code,
                RuleSetVersion = rules.VersionNumber,
                result.Status,
                result.IncompleteReason,
                result.TotalScore,
                result.TierCode,
                ExcludedCount = result.Contributions.Count(contribution => contribution.IsExcluded),
                result.ComputedAt,
                member.BirthMonth.Year,
                member.BirthMonth.Month,
                member.EmploymentStatus,
            };
        if (filter.WaveId is { } waveId)
        {
            query = query.Where(row => row.WaveId == waveId);
        }

        if (filter.InstrumentCode is { } instrumentCode)
        {
            query = query.Where(row => row.InstrumentCode == instrumentCode);
        }

        var rows = await query.OrderBy(row => row.Id).Take(limit).ToListAsync(cancellationToken);
        var sections = await SectionCodesAsync(rows.Select(row => row.InstrumentSectionId), cancellationToken);
        return rows
            .Select(row => new ExportScoreRow(
                row.FamilyMemberId, row.HouseholdId, row.InstrumentCode, row.VersionNumber,
                row.InstrumentSectionId is { } section ? sections[section] : null, row.WaveCode, row.RuleSetCode, row.RuleSetVersion,
                row.Status, row.IncompleteReason, row.TotalScore, row.TierCode, row.ExcludedCount, row.ComputedAt, row.Year, row.Month,
                row.EmploymentStatus))
            .ToList();
    }

    /// <summary>Members with research-use consent active now, in a household that has not withdrawn from the study.</summary>
    private IQueryable<FamilyMemberId> ResearchConsented() =>
        dbContext.ConsentRecords
            .Where(consent => consent.ConsentType == ConsentType.ResearchUse && consent.WithdrawnAt == null)
            .Join(dbContext.Households.Where(household => household.Status == HouseholdStatus.Active),
                consent => consent.HouseholdId, household => household.Id, (consent, _) => consent.FamilyMemberId);

    private async Task<Dictionary<InstrumentSectionId, string>> SectionCodesAsync(
        IEnumerable<InstrumentSectionId?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<InstrumentSectionId>().Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await dbContext.Set<InstrumentSection>().AsNoTracking()
                .Where(section => wanted.Contains(section.Id))
                .ToDictionaryAsync(section => section.Id, section => section.Code, cancellationToken);
    }
}
