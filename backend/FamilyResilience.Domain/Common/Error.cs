namespace FamilyResilience.Domain.Common;

/// <summary>An expected failure: a stable machine-readable code plus a message for logs and developers.</summary>
public sealed record Error(string Code, string Message);
