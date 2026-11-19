using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Households;

/// <summary>Withdraws the household from the study. Its history is kept; its roster can no longer change.</summary>
public sealed class UpdateHousehold(
    ICallerAccess access, IHouseholdRepository households, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<HouseholdResponse>> HandleAsync(
        Guid householdId, UpdateHouseholdRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await ManagedHouseholds.LoadAsync(access, households, householdId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<HouseholdResponse>(loaded.Error!);
        }

        var household = loaded.Value;
        var withdrawn = household.Withdraw(timeProvider.GetUtcNow());
        if (withdrawn.IsFailure)
        {
            return Result.Failure<HouseholdResponse>(withdrawn.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "households.household.withdrawn", "household", household.Id.Value, cancellationToken);
        return committed.IsFailure ? Result.Failure<HouseholdResponse>(committed.Error!) : Result.Success(household.ToResponse());
    }
}
