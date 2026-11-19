namespace FamilyResilience.Application.Households;

/// <summary>
/// The confirmed roster and the household's live assignments in the current wave. <c>waveId</c> is null, and the list
/// empty, when no wave is open.
/// </summary>
public sealed record RosterConfirmationResponse(
    Guid HouseholdId,
    DateTimeOffset RosterConfirmedAt,
    Guid? WaveId,
    string? WaveCode,
    IReadOnlyList<RosterAssignmentResponse> Assignments);
