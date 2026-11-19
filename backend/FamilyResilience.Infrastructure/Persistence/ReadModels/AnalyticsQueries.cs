using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// The overview's fixed aggregates, as LINQ that EF Core translates to GROUP BY and COUNT in PostgreSQL. Only counts
/// leave the database; the wave and the time window are the only inputs. Split by topic across partial files.
/// </summary>
public sealed partial class AnalyticsQueries(FamilyResilienceDbContext dbContext) : IAnalyticsQueries
{
    public async Task<EnrollmentCounts> EnrollmentAsync(CancellationToken cancellationToken = default)
    {
        var byStatus = await dbContext.Households.AsNoTracking()
            .GroupBy(household => household.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var confirmed = await dbContext.Households.AsNoTracking()
            .CountAsync(household => household.RosterConfirmedAt != null, cancellationToken);
        // Members of a household that withdrew from the study no longer take part, though their rows stay Active.
        var activeMembers = await dbContext.Set<FamilyMember>().AsNoTracking()
            .Where(member => member.Status == MemberStatus.Active)
            .Join(dbContext.Households.Where(household => household.Status == HouseholdStatus.Active),
                member => member.HouseholdId, household => household.Id, (member, _) => member.Id)
            .CountAsync(cancellationToken);

        int Count(HouseholdStatus status) => byStatus.Where(row => row.Status == status).Sum(row => row.Count);
        return new EnrollmentCounts(
            byStatus.Sum(row => row.Count), Count(HouseholdStatus.Active), Count(HouseholdStatus.Withdrawn), confirmed, activeMembers);
    }

    /// <summary>Assignments, optionally of one wave, read without tracking.</summary>
    private IQueryable<AssessmentAssignment> Assignments(WaveId? waveId)
    {
        var query = dbContext.AssessmentAssignments.AsNoTracking();
        return waveId is { } wave ? query.Where(assignment => assignment.WaveId == wave) : query;
    }

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
