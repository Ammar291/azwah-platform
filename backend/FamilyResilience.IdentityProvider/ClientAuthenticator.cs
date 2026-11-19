using System.Security.Cryptography;
using System.Text;

namespace FamilyResilience.IdentityProvider;

/// <summary>
/// Client authentication at the token and revocation endpoints (RFC 6749 section 2.3): a confidential client sends its
/// secret by client_secret_basic or client_secret_post (never both); a public client sends only its client_id.
/// </summary>
internal sealed class ClientAuthenticator(ClientRegistry clients)
{
    /// <summary>The authenticated client, or the error to answer with.</summary>
    public (RegisteredClient? Client, OAuthJson? Error) Authenticate(HttpRequest request, IFormCollection form)
    {
        var formClientId = AuthorizationRequestValidator.Value(form["client_id"]);
        var formSecret = AuthorizationRequestValidator.Value(form["client_secret"]);
        var authorization = request.Headers.Authorization;
        if (authorization.Count > 1)
        {
            return (null, OAuthJson.InvalidRequest("Send one Authorization header."));
        }

        if (authorization.Count == 1)
        {
            if (formSecret is not null)
            {
                return (null, OAuthJson.InvalidRequest("Use one client authentication method."));
            }

            if (!TryReadBasic(authorization[0], out var basicId, out var basicSecret)
                || (formClientId is not null && !string.Equals(formClientId, basicId, StringComparison.Ordinal)))
            {
                return (null, OAuthJson.InvalidClient(usedBasic: true));
            }

            var basicClient = clients.Find(basicId);
            return basicClient is { Confidential: true } && SecretMatches(basicClient, basicSecret)
                ? (basicClient, null)
                : (null, OAuthJson.InvalidClient(usedBasic: true));
        }

        var client = clients.Find(formClientId);
        if (client is null)
        {
            return (null, OAuthJson.InvalidClient(usedBasic: false));
        }

        var authenticated = client.Confidential
            ? formSecret is not null && SecretMatches(client, formSecret)
            : formSecret is null;
        return authenticated ? (client, null) : (null, OAuthJson.InvalidClient(usedBasic: false));
    }

    /// <summary>Constant time, and false while the client has no secret configured.</summary>
    private static bool SecretMatches(RegisteredClient client, string presented) =>
        client.Secret is not null
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(client.Secret)));

    /// <summary>"Basic base64(form-urlencoded id ":" form-urlencoded secret)".</summary>
    private static bool TryReadBasic(string? header, out string clientId, out string secret)
    {
        clientId = string.Empty;
        secret = string.Empty;
        const string Scheme = "Basic ";
        if (header is null || !header.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[Scheme.Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        clientId = FormDecode(decoded[..separator]);
        secret = FormDecode(decoded[(separator + 1)..]);
        return secret.Length > 0;
    }

    private static string FormDecode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
