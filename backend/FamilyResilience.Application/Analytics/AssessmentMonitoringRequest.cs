using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Query string for the assessment list: optional wave, household, instrument code, version number (with an instrument
/// code), assignment status and assigned days (UTC, both ends included, from 2000 to 2100 like the overview's window).
/// </summary>
public sealed class AssessmentMonitoringRequest : IPageRequest
{
    public Guid? WaveId { get; init; }

    public Guid? HouseholdId { get; init; }

    public string? InstrumentCode { get; init; }

    public int? VersionNumber { get; init; }

    public AssignmentStatus? Status { get; init; }

    public DateOnly? AssignedFrom { get; init; }

    public DateOnly? AssignedTo { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
