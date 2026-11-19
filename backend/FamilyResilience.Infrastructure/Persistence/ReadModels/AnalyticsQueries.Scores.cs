using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

public sealed partial class AnalyticsQueries
{
    public async Task<IReadOnlyList<TierCount>> TierCountsAsync(WaveId? waveId, CancellationToken cancellationToken = default)
    {
        // Research-use consent that is active now, of a member whose household has not withdrawn from the study.
        var researchConsented = dbContext.ConsentRecords.AsNoTracking()
            .Where(consent => consent.ConsentType == ConsentType.ResearchUse && consent.WithdrawnAt == null)
            .Join(dbContext.Households.Where(household => household.Status == HouseholdStatus.Active),
                consent => consent.HouseholdId, household => household.Id, (consent, _) => consent.FamilyMemberId);
        var scored = dbContext.ScoreResults.AsNoTracking()
            .Where(result => result.Status == ScoreStatus.Complete && result.TierCode != null)
            .Join(dbContext.RuleSets.Where(rules => rules.Kind == RuleSetKind.Scoring && rules.Status == RuleSetStatus.Approved),
                result => result.RuleSetId, rules => rules.Id,
                (result, rules) => new { result.AssessmentSessionId, result.TierCode, RuleSetCode = rules.Code, RuleSetVersion = rules.VersionNumber })
            .Join(dbContext.AssessmentSessions, row => row.AssessmentSessionId, session => session.Id,
                (row, session) => new
                {
                    row.TierCode, row.RuleSetCode, row.RuleSetVersion, session.AssessmentAssignmentId, session.FamilyMemberId,
                    session.InstrumentVersionId, session.InstrumentSectionId,
                })
            .Where(row => researchConsented.Contains(row.FamilyMemberId))
            .Join(Assignments(waveId), row => row.AssessmentAssignmentId, assignment => assignment.Id, (row, _) => row)
            .Join(dbContext.InstrumentVersions, row => row.InstrumentVersionId, version => version.Id,
                (row, version) => new
                {
                    row.TierCode, row.RuleSetCode, row.RuleSetVersion, row.InstrumentSectionId, row.FamilyMemberId, version.VersionNumber,
                    version.InstrumentId,
                })
            .Join(dbContext.Instruments, row => row.InstrumentId, instrument => instrument.Id,
                (row, instrument) => new
                {
                    row.TierCode, row.RuleSetCode, row.RuleSetVersion, row.InstrumentSectionId, row.FamilyMemberId, row.VersionNumber,
                    instrument.Code,
                });

        // A member scored in several waves has several results: each tier, and each distribution as a whole, is also
        // counted in distinct members, which is what small-cell suppression is decided on (D-58).
        var rows = await scored
            .GroupBy(row => new { row.Code, row.VersionNumber, row.InstrumentSectionId, row.RuleSetCode, row.RuleSetVersion, row.TierCode })
            .Select(group => new
            {
                group.Key.Code, group.Key.VersionNumber, group.Key.InstrumentSectionId, group.Key.RuleSetCode, group.Key.RuleSetVersion,
                group.Key.TierCode, Count = group.Count(), Members = group.Select(row => row.FamilyMemberId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        var distributions = await scored
            .GroupBy(row => new { row.Code, row.VersionNumber, row.InstrumentSectionId, row.RuleSetCode, row.RuleSetVersion })
            .Select(group => new
            {
                group.Key.Code, group.Key.VersionNumber, group.Key.InstrumentSectionId, group.Key.RuleSetCode, group.Key.RuleSetVersion,
                Members = group.Select(row => row.FamilyMemberId).Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        var sections = await SectionCodesAsync(rows.Select(row => row.InstrumentSectionId), cancellationToken);
        return rows
            .Select(row => new TierCount(
                row.Code, row.VersionNumber, row.InstrumentSectionId is { } id ? sections[id] : null, row.RuleSetCode, row.RuleSetVersion,
                row.TierCode!, row.Count, row.Members,
                distributions.Single(distribution =>
                    distribution.Code == row.Code
                    && distribution.VersionNumber == row.VersionNumber
                    && distribution.InstrumentSectionId == row.InstrumentSectionId
                    && distribution.RuleSetCode == row.RuleSetCode
                    && distribution.RuleSetVersion == row.RuleSetVersion).Members))
            .ToList();
    }
}
