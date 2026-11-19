namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>A request the provider sent to the fake UAE PASS: method, address, Authorization header and body.</summary>
public sealed record CapturedUaePassRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);
