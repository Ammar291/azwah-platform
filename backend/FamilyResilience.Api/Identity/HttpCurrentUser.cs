using System.Security.Claims;
using FamilyResilience.Application.Abstractions;

namespace FamilyResilience.Api.Identity;

/// <summary>
/// The caller as established by the authentication middleware from a validated token. Outside an HTTP request (hosted
/// services, commands) there is no caller and the platform itself is acting.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? SubjectId => IsAuthenticated ? Principal!.FindFirst(ClaimNames.Subject)?.Value : null;

    public bool IsSystem => httpContextAccessor.HttpContext is null;

    public IReadOnlyCollection<string> Roles => IsAuthenticated
        ? Principal!.FindAll(ClaimNames.Role).Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray()
        : [];
}
