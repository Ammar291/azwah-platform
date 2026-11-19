using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Analytics;

public sealed record VersionStatusCount(string InstrumentCode, int VersionNumber, string? SectionCode, AssignmentStatus Status, int Count);
