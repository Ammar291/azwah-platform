namespace FamilyResilience.Infrastructure;

public static class HealthCheckTags
{
    /// <summary>Checks that must pass before the API takes traffic (readiness).</summary>
    public const string Ready = "ready";
}
