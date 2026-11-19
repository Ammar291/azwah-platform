using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

public sealed class GetMember(ICallerAccess access, IHouseholdRepository households, TimeProvider timeProvider)
{
    public async Task<Result<MemberResponse>> HandleAsync(Guid householdId, Guid memberId, CancellationToken cancellationToken)
    {
        var loaded = await ManagedHouseholds.LoadAsync(access, households, householdId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<MemberResponse>(loaded.Error!);
        }

        var household = loaded.Value;
        var member = household.FindMember(FamilyMemberId.From(memberId));
        if (member is null)
        {
            return Result.Failure<MemberResponse>(HouseholdErrors.MemberNotFound);
        }

        var caller = (await access.GetCallerAsync(cancellationToken))?.UserId;
        return Result.Success(member.ToResponse(household, caller, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)));
    }
}
