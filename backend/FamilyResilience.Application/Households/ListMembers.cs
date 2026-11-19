using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Households;

/// <summary>The roster, active and inactive members, in the order they joined, a page at a time.</summary>
public sealed class ListMembers(ICallerAccess access, IHouseholdRepository households, TimeProvider timeProvider)
{
    public async Task<Result<PagedResponse<MemberResponse>>> HandleAsync(
        Guid householdId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var loaded = await ManagedHouseholds.LoadAsync(access, households, householdId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<PagedResponse<MemberResponse>>(loaded.Error!);
        }

        var household = loaded.Value;
        var caller = (await access.GetCallerAsync(cancellationToken))?.UserId;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var items = household.InRosterOrder()
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(member => member.ToResponse(household, caller, today))
            .ToList();
        return Result.Success(new PagedResponse<MemberResponse>(items, page.Page, page.PageSize, household.Members.Count));
    }
}
