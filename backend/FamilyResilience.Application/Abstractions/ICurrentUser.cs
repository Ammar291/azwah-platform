namespace FamilyResilience.Application.Abstractions;

/// <summary>
/// The caller as established by the server from a validated token. Never build this from IDs or roles sent in a
/// request body or query string.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The token's <c>sub</c> claim; null when not authenticated.</summary>
    string? SubjectId { get; }

    IReadOnlyCollection<string> Roles { get; }

    /// <summary>True when the platform itself is acting (a hosted service or command), not a request.</summary>
    bool IsSystem { get; }
}
