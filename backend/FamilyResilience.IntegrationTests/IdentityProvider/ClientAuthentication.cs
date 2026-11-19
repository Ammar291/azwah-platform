namespace FamilyResilience.IntegrationTests.IdentityProvider;

/// <summary>How a test client authenticates at the token and revocation endpoints.</summary>
public enum ClientAuthentication
{
    /// <summary>A public client: only <c>client_id</c> in the body.</summary>
    None,

    /// <summary><c>client_secret_basic</c>: the HTTP Basic header.</summary>
    Basic,

    /// <summary><c>client_secret_post</c>: <c>client_id</c> and <c>client_secret</c> in the body.</summary>
    Post,
}
