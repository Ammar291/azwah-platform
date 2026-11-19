namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Which of the provider's two modes the host runs in, decided by the environment alone (D-129). In Development the
/// simulated personas are available and every token and page carries the development markers; anywhere else UAE PASS
/// is the sign-in (with Google beside it when configured, D-147) and nothing is simulated. There is no setting that turns
/// personas on outside Development.
/// </summary>
internal sealed class ProviderMode(IHostEnvironment environment)
{
    /// <summary>The persona page and the persona choice exist.</summary>
    public bool PersonasAvailable => environment.IsDevelopment();

    /// <summary>Not Development: UAE PASS only, https only, a configured signing key.</summary>
    public bool IsDeployed => !PersonasAvailable;

    /// <summary><c>azwah_development_only</c> in discovery and tokens, and the simulated banner on pages.</summary>
    public bool DevelopmentMarkers => PersonasAvailable;

    /// <summary>
    /// The Production environment. While only UAE PASS staging is allowed (test identities), a production provider
    /// would register test accounts as real participants (CONTRIBUTING.md rule 15), so start-up refuses it (D-129).
    /// </summary>
    public bool IsProduction => environment.IsProduction();
}
