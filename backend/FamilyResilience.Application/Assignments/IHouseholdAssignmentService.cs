using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// The eligibility application service: evaluates every member of a household against the approved eligibility rules
/// in effect and applies the plan for one wave. Call it inside the caller's
/// <c>IUnitOfWork.ExecuteInTransactionAsync</c>: it locks the household, commits its changes there, and leaves the
/// final commit or rollback to the caller's transaction.
/// </summary>
public interface IHouseholdAssignmentService
{
    Task<Result<AssignmentRunResponse>> AssignAsync(HouseholdId householdId, WaveId waveId, CancellationToken cancellationToken);
}
