namespace FamilyResilience.IdentityProvider;

internal enum GrantOutcome
{
    Accepted,

    /// <summary>Unknown, expired, revoked, or issued to another client.</summary>
    Invalid,

    /// <summary>A code or refresh token was presented again after it was used: everything it led to is revoked.</summary>
    Reused,

    /// <summary>A refresh asked for scope beyond what was granted.</summary>
    ScopeExceeded,
}
