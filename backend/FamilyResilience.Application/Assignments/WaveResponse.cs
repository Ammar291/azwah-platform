using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Assignments;

/// <summary>A wave as staff see it in filters: no counts, nothing about who takes part.</summary>
public sealed record WaveResponse(Guid Id, string Code, WaveStatus Status, DateTimeOffset OpensAt, DateTimeOffset ClosesAt);
