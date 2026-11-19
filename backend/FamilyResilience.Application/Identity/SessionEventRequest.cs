namespace FamilyResilience.Application.Identity;

/// <summary>Which of the platform's apps the caller signed in to or out of: <c>admin</c> or <c>mobile</c>.</summary>
public sealed record SessionEventRequest(string? Client);
