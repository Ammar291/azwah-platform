namespace FamilyResilience.IdentityProvider;

/// <summary>
/// The provider's log: its own sign-in record for development. It names personas, clients and fixed reason codes only,
/// never tokens, codes, secrets, query strings or anything UAE PASS returned about a person.
/// </summary>
internal static partial class IdentityProviderLog
{
    public const string Category = "FamilyResilience.IdentityProvider";

    public static ILogger For(ILoggerFactory loggers) => loggers.CreateLogger(Category);

    [LoggerMessage(EventId = 3000, Level = LogLevel.Warning,
        Message = "Development identity provider started for issuer {Issuer}: sign-in is simulated and no identity is checked. Never use it outside local development.")]
    public static partial void Started(ILogger logger, string issuer);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Client {ClientId} is confidential but has no secret configured; its token and revocation requests fail with invalid_client.")]
    public static partial void ClientWithoutSecret(ILogger logger, string clientId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Signed in persona {Persona} for client {ClientId}.")]
    public static partial void SignedIn(ILogger logger, string persona, string clientId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information, Message = "Refreshed tokens of persona {Persona} for client {ClientId}.")]
    public static partial void Refreshed(ILogger logger, string persona, string clientId);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning,
        Message = "An authorization code was presented again by client {ClientId}; the refresh tokens it issued are revoked.")]
    public static partial void CodeReused(ILogger logger, string clientId);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning,
        Message = "A rotated refresh token was presented again by client {ClientId}; its whole family is revoked.")]
    public static partial void RefreshTokenReused(ILogger logger, string clientId);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Information, Message = "Signed out for client {ClientId}.")]
    public static partial void SignedOut(ILogger logger, string clientId);

    [LoggerMessage(EventId = 3007, Level = LogLevel.Warning,
        Message = "UAE PASS (staging) sign-in is enabled with callback {CallbackUri}: staging test accounts can sign in, and their ID tokens carry a masked profile.")]
    public static partial void UaePassEnabled(ILogger logger, string callbackUri);

    /// <param name="reason">A fixed code such as <c>token_rejected</c> or <c>userinfo_timeout</c>: never a value from UAE PASS.</param>
    [LoggerMessage(EventId = 3008, Level = LogLevel.Warning, Message = "UAE PASS (staging) sign-in for client {ClientId} failed: {Reason}.")]
    public static partial void UaePassFailed(ILogger logger, string reason, string clientId);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Information,
        Message = "Identity provider started for issuer {Issuer}: UAE PASS staging is the sign-in (and Google when configured); nothing is simulated.")]
    public static partial void StartedDeployed(ILogger logger, string issuer);

    [LoggerMessage(EventId = 3010, Level = LogLevel.Information, Message = "Signed out of UAE PASS (staging) for client {ClientId}.")]
    public static partial void UaePassSignedOut(ILogger logger, string clientId);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Warning,
        Message = "UAE PASS (staging) is enabled but IdentityProvider:UaePass:ClientId or ClientSecret is empty; UAE PASS sign-in stays off.")]
    public static partial void UaePassWithoutCredentials(ILogger logger);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Warning,
        Message = "Google sign-in is enabled with callback {CallbackUri}: Google accounts can sign in as participants, and their ID tokens carry a masked profile.")]
    public static partial void GoogleEnabled(ILogger logger, string callbackUri);

    /// <param name="reason">A fixed code such as <c>token_rejected</c> or <c>userinfo_timeout</c>: never a value from Google.</param>
    [LoggerMessage(EventId = 3013, Level = LogLevel.Warning, Message = "Google sign-in for client {ClientId} failed: {Reason}.")]
    public static partial void GoogleFailed(ILogger logger, string reason, string clientId);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Warning,
        Message = "Google sign-in is enabled but IdentityProvider:Google:ClientId or ClientSecret is empty; Google sign-in stays off.")]
    public static partial void GoogleWithoutCredentials(ILogger logger);

    /// <param name="roles">The configured role codes (IdentityProvider:Google:StaffRoles), comma-separated.</param>
    [LoggerMessage(EventId = 3015, Level = LogLevel.Warning,
        Message = "Google staff sign-in is on for client {ClientId} (Development only, D-149): every Google account the Google OAuth client lets sign in (its test users while it is in Testing) signs in to this client as staff with roles {Roles}. Synthetic demonstration data only; never with real families.")]
    public static partial void GoogleStaffSignInEnabled(ILogger logger, string clientId, string roles);
}
