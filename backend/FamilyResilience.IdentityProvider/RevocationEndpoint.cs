namespace FamilyResilience.IdentityProvider;

/// <summary>
/// <c>POST /revoke</c> (RFC 7009). A refresh token ends its whole family. The answer is 200 whether the token was
/// known, already revoked, an access token (self-contained, so it simply runs out) or another client's (left alone).
/// Client authentication still applies: a confidential client without its secret gets invalid_client.
/// </summary>
internal static class RevocationEndpoint
{
    public static void MapRevocation(this IEndpointRouteBuilder endpoints) => endpoints.MapPost("/revoke", HandleAsync);

    private static async Task<IResult> HandleAsync(HttpContext context, ClientAuthenticator authenticator, GrantStore grants)
    {
        var form = await FormRequests.ReadAsync(context.Request);
        if (form is null)
        {
            return OAuthJson.InvalidRequest("The body must be application/x-www-form-urlencoded.");
        }

        if (FormRequests.HasRepeatedFields(form))
        {
            return OAuthJson.InvalidRequest("A parameter appears more than once.");
        }

        var (client, error) = authenticator.Authenticate(context.Request, form);
        if (client is null)
        {
            return error!;
        }

        var token = AuthorizationRequestValidator.Value(form["token"]);
        if (token is null)
        {
            return OAuthJson.InvalidRequest("token is required.");
        }

        grants.Revoke(token, client.Id);
        return OAuthJson.Empty();
    }
}
