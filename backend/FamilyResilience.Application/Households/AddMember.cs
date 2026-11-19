using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Households;

/// <summary>Adds a member to the roster. The roster then needs confirming again before eligibility is re-run.</summary>
public sealed class AddMember(
    ICallerAccess access, IHouseholdRepository households, IUnitOfWork unitOfWork, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<MemberResponse>> HandleAsync(Guid householdId, MemberDetailsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var loaded = await ManagedHouseholds.LoadAsync(access, households, householdId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<MemberResponse>(loaded.Error!);
        }

        var household = loaded.Value;
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var details = MemberDetailsRules.ToDetails(request, today);
        if (details.IsFailure)
        {
            return Result.Failure<MemberResponse>(details.Error!);
        }

        var added = household.AddMember(details.Value, now);
        if (added.IsFailure)
        {
            return Result.Failure<MemberResponse>(added.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "households.member.added", "family_member", added.Value.Id.Value, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<MemberResponse>(committed.Error!);
        }

        var caller = (await access.GetCallerAsync(cancellationToken))?.UserId;
        return Result.Success(added.Value.ToResponse(household, caller, today));
    }
}
