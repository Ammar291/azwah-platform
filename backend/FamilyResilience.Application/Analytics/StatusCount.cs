using FamilyResilience.Domain.Assignments;

namespace FamilyResilience.Application.Analytics;

public sealed record StatusCount(AssignmentStatus Status, int Count);
