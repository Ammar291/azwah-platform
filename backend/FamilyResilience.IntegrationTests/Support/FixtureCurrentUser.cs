using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>The caller for persistence tests, which run without an HTTP request.</summary>
public sealed class FixtureCurrentUser : ICurrentUser
{
    public const string Subject = "integration-test-user";

    public bool IsAuthenticated => true;

    public string? SubjectId => Subject;

    public IReadOnlyCollection<string> Roles { get; } = [];

    public bool IsSystem => false;
}
