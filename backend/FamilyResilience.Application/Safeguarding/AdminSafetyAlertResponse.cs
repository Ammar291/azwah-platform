using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Safeguarding;

/// <summary>
/// A safety alert as a system administrator sees it: fixed codes only, never what the person said. The household
/// reference is the one the family can quote. <c>memberRelationship</c> is the member's relationship to the head as the
/// roster records it (<c>spouse</c>, <c>child</c>, … <c>non_relative</c>), <see cref="HeadOfHousehold"/> for the head
/// (who has no relationship value), and null when the alert names no member.
/// </summary>
public sealed record AdminSafetyAlertResponse(
    Guid Id,
    string? HouseholdReference,
    string? MemberRelationship,
    SafetyAlertSource Source,
    SafetyConcern Category,
    SafetySeverity Severity,
    SafetyDetector Detector,
    string? SignalCode,
    string? Language,
    int OccurrenceCount,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastDetectedAt,
    SafetyAlertStatus Status,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    SafetyAlertResolution? Resolution)
{
    /// <summary>The <c>memberRelationship</c> of the head of household.</summary>
    public const string HeadOfHousehold = "head";
}
