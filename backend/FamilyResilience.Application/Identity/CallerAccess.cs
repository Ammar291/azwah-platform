using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// The production access rule. A caller without a subject, without a platform user or without an active membership
/// reaches nothing; otherwise the household aggregate decides through <see cref="Household.MayActFor"/>.
/// </summary>
public sealed class CallerAccess(ICurrentUser currentUser, IAccessQueries queries) : ICallerAccess, IParticipantAccess
{
    private Task<CallerRecord?>? _caller;

    public Task<CallerRecord?> GetCallerAsync(CancellationToken cancellationToken = default) =>
        _caller ??= LoadAsync(cancellationToken);

    public async Task<bool> CanManageHouseholdAsync(HouseholdId householdId, CancellationToken cancellationToken = default) =>
        await GetCallerAsync(cancellationToken) is { Household: { } household } caller
        && household.Id == householdId
        && household.IsManagedBy(caller.UserId);

    public async Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken = default) =>
        await GetCallerAsync(cancellationToken) is { Household: { } household } caller
        && household.MayActFor(caller.UserId, memberId);

    public async Task<bool> IsHeadOfHouseholdAsync(CancellationToken cancellationToken = default) =>
        await GetCallerAsync(cancellationToken) is { Household: { } household } caller
        && household.IsHeadOfHousehold(caller.UserId);

    public async Task<IReadOnlyList<FamilyMemberId>> ActableMemberIdsAsync(CancellationToken cancellationToken = default) =>
        await GetCallerAsync(cancellationToken) is { Household: { } household } caller
            ? household.MembersActableBy(caller.UserId)
            : [];

    private async Task<CallerRecord?> LoadAsync(CancellationToken cancellationToken) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.SubjectId)
            ? await queries.FindCallerAsync(currentUser.SubjectId, cancellationToken)
            : null;
}
