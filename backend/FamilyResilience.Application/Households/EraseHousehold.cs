using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Privacy;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Households;

/// <summary>
/// Erases one household (D-88), for an approved request (the <c>db erase-household</c> command) or the end of an
/// approved retention period (<c>ApplyRetention</c>). It refuses unless <see cref="ErasureSettings"/> says what
/// happens to consent records, the caller gives the household's reference, and the household has withdrawn (or the
/// operator asks to withdraw it first, which is audited too). Everything happens in one transaction, after a lock on
/// the household's row (a second erasure of it then finds nothing and answers not found): the withdrawal, a
/// <c>privacy.request.closed</c> event for each of its open privacy requests (they are honoured by the erasure, and
/// deleted with it), every delete (<see cref="IHouseholdErasure"/>) and the <c>households.household.erased</c> event.
/// Audit events about the household stay; they hold opaque IDs and actor subjects only.
/// </summary>
public sealed class EraseHousehold(
    IHouseholdRepository households,
    IHouseholdErasure erasure,
    IPrivacyRequestRepository privacyRequests,
    IAuditLog auditLog,
    IUnitOfWork unitOfWork,
    ErasureSettings settings,
    TimeProvider timeProvider)
{
    public const string Erased = "households.household.erased";

    public async Task<Result<IReadOnlyDictionary<string, int>>> HandleAsync(
        Guid householdId, string? reference, bool withdrawFirst, CancellationToken cancellationToken)
    {
        if (!settings.DeletesConsentRecords)
        {
            return Result.Failure<IReadOnlyDictionary<string, int>>(HouseholdErrors.ErasureNotConfigured);
        }

        var id = HouseholdId.From(householdId);
        var household = await households.GetAsync(id, cancellationToken);
        if (household is null)
        {
            return Result.Failure<IReadOnlyDictionary<string, int>>(HouseholdErrors.HouseholdNotFound);
        }

        if (!string.Equals(household.Reference.Value, reference?.Trim(), StringComparison.Ordinal))
        {
            return Result.Failure<IReadOnlyDictionary<string, int>>(HouseholdErrors.ErasureReferenceMismatch);
        }

        if (household.Status != HouseholdStatus.Withdrawn && !withdrawFirst)
        {
            return Result.Failure<IReadOnlyDictionary<string, int>>(HouseholdErrors.ErasureNeedsWithdrawal);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (!await erasure.LockAsync(id, token))
                {
                    return Result.Failure<IReadOnlyDictionary<string, int>>(HouseholdErrors.HouseholdNotFound);
                }

                if (household.Status != HouseholdStatus.Withdrawn)
                {
                    var withdrawn = household.Withdraw(timeProvider.GetUtcNow());
                    var stagedWithdrawal = withdrawn.IsFailure
                        ? withdrawn
                        : auditLog.Stage(new AuditRecord("households.household.withdrawn", "household", householdId.ToString(), AuditOutcome.Succeeded));
                    var committedWithdrawal = stagedWithdrawal.IsFailure ? stagedWithdrawal : await unitOfWork.CommitAsync(token);
                    if (committedWithdrawal.IsFailure)
                    {
                        return Result.Failure<IReadOnlyDictionary<string, int>>(committedWithdrawal.Error!);
                    }
                }

                foreach (var requestId in await privacyRequests.ListOpenIdsForHouseholdAsync(id, token))
                {
                    var closed = auditLog.Stage(new AuditRecord(
                        ClosePrivacyRequest.Closed, ClosePrivacyRequest.ResourceType, requestId.Value.ToString(), AuditOutcome.Succeeded));
                    if (closed.IsFailure)
                    {
                        return Result.Failure<IReadOnlyDictionary<string, int>>(closed.Error!);
                    }
                }

                var counts = await erasure.EraseAsync(id, token);
                var staged = auditLog.Stage(new AuditRecord(Erased, "household", householdId.ToString(), AuditOutcome.Succeeded));
                var committed = staged.IsFailure ? staged : await unitOfWork.CommitAsync(token);
                return committed.IsFailure
                    ? Result.Failure<IReadOnlyDictionary<string, int>>(committed.Error!)
                    : Result.Success(counts);
            },
            cancellationToken);
    }
}
