namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>A request the provider sent to the fake Google: method, address, Authorization header and body.</summary>
public sealed record CapturedGoogleRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);
