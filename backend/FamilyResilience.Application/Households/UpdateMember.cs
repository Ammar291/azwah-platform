using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>
/// Applies a merge patch to one active member, or removes them from the roster. Either change clears the roster
/// confirmation. The household's manager can never be removed. A member who holds any active consent cannot be recorded
/// as under 18: only adults consent and answer (D-58), so their consents are withdrawn first. Removing a member also
/// closes what they left open (<see cref="IMemberAssessmentCloser"/>), in one transaction that first locks the household
/// as assignment runs do (D-44): the removal commits, then their sessions in progress are abandoned and their open
/// assignments cancelled; if any step fails, nothing changes.
/// </summary>
public sealed class UpdateMember(
    ICallerAccess access,
    IHouseholdRepository households,
    IHouseholdRunLock runLock,
    IConsentRecordRepository consents,
    IMemberAssessmentCloser assessments,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<MemberResponse>> HandleAsync(
        Guid householdId, Guid memberId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Status.IsSet)
        {
            return await DeactivateAsync(HouseholdId.From(householdId), FamilyMemberId.From(memberId), cancellationToken);
        }

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

        var now = timeProvider.GetUtcNow();
        var changed = await PatchAsync(household, member, request, now, cancellationToken);
        if (changed.IsFailure)
        {
            return Result.Failure<MemberResponse>(changed.Error!);
        }

        var committed = await AuditedCommit.RunAsync(auditLog, unitOfWork, "households.member.updated", "family_member", memberId, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<MemberResponse>(committed.Error!)
            : Result.Success(await ResponseAsync(household, member, now, cancellationToken));
    }

    private async Task<Result<MemberResponse>> DeactivateAsync(
        HouseholdId householdId, FamilyMemberId memberId, CancellationToken cancellationToken)
    {
        // Another family's household answers "not found", as ManagedHouseholds does, before anything is locked.
        if (!await access.CanManageHouseholdAsync(householdId, cancellationToken))
        {
            return Result.Failure<MemberResponse>(HouseholdErrors.HouseholdNotFound);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Locked before it is read: an assignment run for this household waits, then finds the member inactive.
                await runLock.LockAsync(householdId, token);
                var household = await households.GetAsync(householdId, token);
                var member = household?.FindMember(memberId);
                if (household is null || member is null)
                {
                    return Result.Failure<MemberResponse>(household is null ? HouseholdErrors.HouseholdNotFound : HouseholdErrors.MemberNotFound);
                }

                var now = timeProvider.GetUtcNow();
                var deactivated = household.DeactivateMember(memberId, now);
                if (deactivated.IsFailure)
                {
                    return Result.Failure<MemberResponse>(deactivated.Error!);
                }

                var committed = await AuditedCommit.RunAsync(
                    auditLog, unitOfWork, "households.member.deactivated", "family_member", memberId.Value, token);
                if (committed.IsFailure)
                {
                    return Result.Failure<MemberResponse>(committed.Error!);
                }

                var closed = await assessments.CloseAsync(memberId, now, token);
                return closed.IsFailure
                    ? Result.Failure<MemberResponse>(closed.Error!)
                    : Result.Success(await ResponseAsync(household, member, now, token));
            },
            cancellationToken);
    }

    private async Task<MemberResponse> ResponseAsync(Household household, FamilyMember member, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var caller = (await access.GetCallerAsync(cancellationToken))?.UserId;
        return member.ToResponse(household, caller, DateOnly.FromDateTime(now.UtcDateTime));
    }

    private async Task<Result> PatchAsync(
        Household household, FamilyMember member, UpdateMemberRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var birth = YearMonth.Create(request.BirthYear.Or(member.BirthMonth.Year), request.BirthMonth.Or(member.BirthMonth.Month), today);
        if (birth.IsFailure)
        {
            return Result.Failure(birth.Error!);
        }

        // The same certainty the consent itself needed: a birth month that leaves the member possibly 17 is refused.
        if (!birth.Value.IsCertainlyAtLeast(FamilyMember.AdultAge, today) && await HasActiveConsentAsync(member.Id, cancellationToken))
        {
            return Result.Failure(HouseholdErrors.MemberHasConsent);
        }

        return household.UpdateMember(
            member.Id,
            new FamilyMemberDetails(
                birth.Value,
                request.RelationshipToHousehold.Or(member.RelationshipToHousehold),
                request.EmploymentStatus.Or(member.EmploymentStatus),
                request.IsHeadOfHousehold.Or(member.IsHeadOfHousehold),
                request.IsPrimaryIncomeEarner.Or(member.IsPrimaryIncomeEarner)),
            now);
    }

    private async Task<bool> HasActiveConsentAsync(FamilyMemberId memberId, CancellationToken cancellationToken)
    {
        foreach (var type in Enum.GetValues<ConsentType>())
        {
            if (await consents.GetActiveAsync(memberId, type, cancellationToken) is not null)
            {
                return true;
            }
        }

        return false;
    }
}
