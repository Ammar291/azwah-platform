using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Analytics;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FamilyResilience.Infrastructure.Persistence.ReadModels;

/// <summary>
/// Program monitoring. Filters, counts and paging run in PostgreSQL; the page's assignment counts and live sessions are
/// then read in one grouped query each. Nothing here reads answers or scores beyond whether a session was scored.
/// </summary>
public sealed class MonitoringReadModel(FamilyResilienceDbContext dbContext) : IMonitoringReadModel
{
    public async Task<PagedResponse<ParticipationRowResponse>> ListParticipationAsync(
        ParticipationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = dbContext.Households.AsNoTracking();
        if (request.Status is { } status)
        {
            query = query.Where(household => household.Status == status);
        }

        if (request.RosterConfirmed is { } confirmed)
        {
            query = confirmed
                ? query.Where(household => household.RosterConfirmedAt != null)
                : query.Where(household => household.RosterConfirmedAt == null);
        }

        if (request.Reference is { } text && HouseholdReference.Parse(text) is { IsSuccess: true } reference)
        {
            query = query.Where(household => household.Reference == reference.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await query
            .OrderByDescending(household => household.CreatedAt)
            .ThenByDescending(household => household.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(household => new
            {
                household.Id,
                household.Reference,
                household.Status,
                household.CreatedAt,
                household.RosterConfirmedAt,
                ActiveMembers = household.Members.Count(member => member.Status == MemberStatus.Active),
            })
            .ToListAsync(cancellationToken);

        var householdIds = page.Select(household => household.Id).ToList();
        var counts = await (
                from assignment in dbContext.AssessmentAssignments.AsNoTracking()
                join member in dbContext.Set<FamilyMember>() on assignment.FamilyMemberId equals member.Id
                where householdIds.Contains(member.HouseholdId)
                group assignment by new { member.HouseholdId, assignment.Status } into grouped
                select new { grouped.Key.HouseholdId, grouped.Key.Status, Count = grouped.Count() })
            .ToListAsync(cancellationToken);

        int Count(HouseholdId householdId, AssignmentStatus wanted) =>
            counts.Where(row => row.HouseholdId == householdId && row.Status == wanted).Sum(row => row.Count);

        return new PagedResponse<ParticipationRowResponse>(
            page.Select(household => new ParticipationRowResponse(
                household.Id.Value,
                household.Reference.Value,
                household.Status,
                household.CreatedAt,
                household.RosterConfirmedAt,
                household.ActiveMembers,
                new AssignmentCountsResponse(
                    Count(household.Id, AssignmentStatus.Assigned),
                    Count(household.Id, AssignmentStatus.InProgress),
                    Count(household.Id, AssignmentStatus.Completed),
                    Count(household.Id, AssignmentStatus.Expired),
                    Count(household.Id, AssignmentStatus.Cancelled)))).ToList(),
            request.Page,
            request.PageSize,
            total);
    }

    public async Task<PagedResponse<AssessmentRowResponse>> ListAssessmentsAsync(
        AssessmentMonitoringRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query =
            from assignment in dbContext.AssessmentAssignments.AsNoTracking()
            join member in dbContext.Set<FamilyMember>() on assignment.FamilyMemberId equals member.Id
            join version in dbContext.InstrumentVersions on assignment.InstrumentVersionId equals version.Id
            join instrument in dbContext.Instruments on version.InstrumentId equals instrument.Id
            join wave in dbContext.Waves on assignment.WaveId equals wave.Id
            select new
            {
                assignment.Id,
                assignment.WaveId,
                member.HouseholdId,
                InstrumentCode = instrument.Code,
                version.VersionNumber,
                assignment.InstrumentSectionId,
                WaveCode = wave.Code,
                assignment.Status,
                assignment.AssignedAt,
                assignment.ExpiresAt,
                assignment.CompletedAt,
            };
        if (request.WaveId is { } waveId)
        {
            var wave = WaveId.From(waveId);
            query = query.Where(row => row.WaveId == wave);
        }

        if (request.HouseholdId is { } householdId)
        {
            var household = HouseholdId.From(householdId);
            query = query.Where(row => row.HouseholdId == household);
        }

        if (request.InstrumentCode is { } code)
        {
            query = query.Where(row => row.InstrumentCode == code);
        }

        if (request.VersionNumber is { } versionNumber)
        {
            query = query.Where(row => row.VersionNumber == versionNumber);
        }

        if (request.Status is { } status)
        {
            query = query.Where(row => row.Status == status);
        }

        if (request.AssignedFrom is { } from)
        {
            var start = StartOf(from);
            query = query.Where(row => row.AssignedAt >= start);
        }

        if (request.AssignedTo is { } to)
        {
            var end = StartOf(to.AddDays(1));
            query = query.Where(row => row.AssignedAt < end);
        }

        var total = await query.CountAsync(cancellationToken);
        var page = await query
            .OrderByDescending(row => row.AssignedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var assignmentIds = page.Select(row => row.Id).ToList();
        var sessions = await dbContext.AssessmentSessions.AsNoTracking()
            .Where(session => assignmentIds.Contains(session.AssessmentAssignmentId) && session.Status != SessionStatus.Abandoned)
            .Select(session => new { session.AssessmentAssignmentId, session.Status, session.SubmittedAt })
            .ToDictionaryAsync(session => session.AssessmentAssignmentId, cancellationToken);
        var sectionIds = page.Select(row => row.InstrumentSectionId).OfType<InstrumentSectionId>().Distinct().ToList();
        var sectionCodes = await dbContext.Set<InstrumentSection>().AsNoTracking()
            .Where(section => sectionIds.Contains(section.Id))
            .ToDictionaryAsync(section => section.Id, section => section.Code, cancellationToken);

        return new PagedResponse<AssessmentRowResponse>(
            page.Select(row =>
            {
                var session = sessions.GetValueOrDefault(row.Id);
                return new AssessmentRowResponse(
                    row.Id.Value,
                    row.HouseholdId.Value,
                    row.InstrumentCode,
                    row.VersionNumber,
                    row.InstrumentSectionId is { } sectionId ? sectionCodes[sectionId] : null,
                    row.WaveCode,
                    row.Status,
                    row.AssignedAt,
                    row.ExpiresAt,
                    row.CompletedAt,
                    session?.Status,
                    session?.SubmittedAt,
                    session?.Status == SessionStatus.Scored);
            }).ToList(),
            request.Page,
            request.PageSize,
            total);
    }

    private static DateTimeOffset StartOf(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
