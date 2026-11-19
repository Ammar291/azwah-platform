namespace FamilyResilience.IntegrationTests.IdentityProvider;

public sealed record ClientCredentials(string ClientId, string? Secret, ClientAuthentication Method)
{
    public static ClientCredentials Mobile { get; } = new(IdentityProviderFlow.MobileClientId, null, ClientAuthentication.None);

    public static ClientCredentials AdminBasic(string secret = IdentityProviderFactory.AdminClientSecret) =>
        new(IdentityProviderFlow.AdminClientId, secret, ClientAuthentication.Basic);

    public static ClientCredentials AdminPost(string secret = IdentityProviderFactory.AdminClientSecret) =>
        new(IdentityProviderFlow.AdminClientId, secret, ClientAuthentication.Post);
}
