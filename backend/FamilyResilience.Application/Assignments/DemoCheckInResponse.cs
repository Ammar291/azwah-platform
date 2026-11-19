namespace FamilyResilience.Application.Assignments;

/// <summary>
/// DEMONSTRATIONS ONLY (D-144). The check-in to answer next: a round made for this household (<see cref="Opened"/>), or
/// the one still open, returned again so a second tap or a retry makes no second round. <see cref="ActiveSessionId"/> is
/// its session in progress, if one was started.
/// </summary>
public sealed record DemoCheckInResponse(Guid AssignmentId, Guid? ActiveSessionId, string WaveCode, bool Opened);
