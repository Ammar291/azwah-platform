using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// The caller's identity as the server established it, their household from the platform's records, and when they were
/// registered (null until they confirm their details or create a household, D-97).
/// </summary>
public sealed class GetMe(ICurrentUser currentUser, ICallerAccess access, IPlatformUserRepository users)
{
    public async Task<Result<MeResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var subject = currentUser.SubjectId ?? throw new InvalidOperationException("GET /me requires a signed-in caller.");
        var roles = currentUser.Roles.Order(StringComparer.Ordinal).ToArray();
        var caller = await access.GetCallerAsync(cancellationToken);
        var registeredAt = caller is null ? null : await users.FindRegisteredAtAsync(subject, cancellationToken);
        if (caller?.Household is not { } household || household.FindActiveMembership(caller.UserId) is not { } membership)
        {
            return Result.Success(new MeResponse(subject, roles, null, registeredAt));
        }

        return Result.Success(new MeResponse(
            subject,
            roles,
            new MyHouseholdResponse(
                household.Id.Value,
                household.Reference.Value,
                household.Status,
                membership.Role,
                membership.FamilyMemberId.Value,
                household.RosterConfirmedAt,
                household.MembersActableBy(caller.UserId).Select(member => member.Value).ToList()),
            registeredAt));
    }
}
