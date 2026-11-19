using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Assistant;

/// <summary>A questionnaire a member was asked to answer, with names in the context's language.</summary>
public sealed record FamilyContextQuestionnaire(
    string Member,
    string InstrumentName,
    string? SectionTitle,
    string Wave,
    AssignmentStatus Status,
    DateTimeOffset AssignedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt);
