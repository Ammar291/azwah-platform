using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>
/// Enrols the caller: registers their platform user on first use, creates the household from the roster and makes the
/// caller its manager, linked to the member marked <c>isSelf</c>. A caller who already belongs to a household cannot
/// create another (one household per account).
/// </summary>
public sealed class CreateHousehold(
    ICurrentUser currentUser,
    ICallerAccess access,
    IPlatformUserRepository users,
    IHouseholdRepository households,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private const int ReferenceAttempts = 5;

    public async Task<Result<HouseholdResponse>> HandleAsync(CreateHouseholdRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Members);

        var now = timeProvider.GetUtcNow();
        var caller = await access.GetCallerAsync(cancellationToken);
        if (caller?.Household is not null)
        {
            return Result.Failure<HouseholdResponse>(HouseholdErrors.AlreadyMember);
        }

        var userId = caller?.UserId;
        if (userId is null)
        {
            var registered = PlatformUser.Register(currentUser.SubjectId, now);
            if (registered.IsFailure)
            {
                return Result.Failure<HouseholdResponse>(registered.Error!);
            }

            users.Add(registered.Value);
            userId = registered.Value.Id;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var roster = new List<FamilyMemberDetails>();
        foreach (var member in request.Members)
        {
            var details = MemberDetailsRules.ToDetails(member, today);
            if (details.IsFailure)
            {
                return Result.Failure<HouseholdResponse>(details.Error!);
            }

            roster.Add(details.Value);
        }

        var selfIndex = request.Members.ToList().FindIndex(member => member.IsSelf == true);
        var household = Household.Enrol(await UnusedReferenceAsync(cancellationToken), roster, selfIndex, userId.Value, now);
        if (household.IsFailure)
        {
            return Result.Failure<HouseholdResponse>(household.Error!);
        }

        households.Add(household.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "households.household.created", "household", household.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<HouseholdResponse>(committed.Error!)
            : Result.Success(household.Value.ToResponse());
    }

    // References are random; a clash is unlikely and the unique index still refuses one that slips through.
    private async Task<HouseholdReference> UnusedReferenceAsync(CancellationToken cancellationToken)
    {
        var reference = HouseholdReference.Generate();
        for (var attempt = 1; attempt < ReferenceAttempts && await households.ReferenceExistsAsync(reference, cancellationToken); attempt++)
        {
            reference = HouseholdReference.Generate();
        }

        return reference;
    }
}
