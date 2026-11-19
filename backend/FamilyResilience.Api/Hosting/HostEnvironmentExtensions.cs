namespace FamilyResilience.Api.Hosting;

public static class HostEnvironmentExtensions
{
    /// <summary>The integration test host's environment name.</summary>
    public const string Testing = "Testing";

    /// <summary>
    /// Any environment other than a developer's machine (Development) or the test host (Testing): staging, production
    /// or anything else. Deployed hosts get the transport rules and the start-up guard; there is no setting to turn
    /// them off, so a deployment must never run as Development or Testing.
    /// </summary>
    public static bool IsDeployed(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return !environment.IsDevelopment() && !environment.IsEnvironment(Testing);
    }
}
