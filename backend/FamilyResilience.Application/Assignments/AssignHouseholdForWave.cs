using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// The staff assignment run for one household and wave, in its own transaction. The run itself is
/// <see cref="IHouseholdAssignmentService"/>, which roster confirmation also uses.
/// </summary>
public sealed class AssignHouseholdForWave(IHouseholdAssignmentService assignmentService, IUnitOfWork unitOfWork)
{
    public Task<Result<AssignmentRunResponse>> HandleAsync(Guid householdId, HouseholdWaveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return unitOfWork.ExecuteInTransactionAsync(
            token => assignmentService.AssignAsync(HouseholdId.From(householdId), WaveId.From(request.WaveId), token),
            cancellationToken);
    }
}
