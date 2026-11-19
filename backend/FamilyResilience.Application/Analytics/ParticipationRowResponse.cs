using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// One household's participation for program monitoring: status, roster confirmation and how far its assessments have
/// got. No member details, answers or scores.
/// </summary>
public sealed record ParticipationRowResponse(
    Guid HouseholdId,
    string Reference,
    HouseholdStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RosterConfirmedAt,
    int ActiveMemberCount,
    AssignmentCountsResponse Assignments);
