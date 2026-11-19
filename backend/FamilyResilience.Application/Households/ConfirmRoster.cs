using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Assignments;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Households;

/// <summary>
/// Confirms the roster and assigns the current wave from approved eligibility rules, in one transaction: lock the
/// household, check and persist the confirmation, run the eligibility service, commit. If the run fails, the
/// confirmation rolls back too. Eligibility is decided here only: the client never sends eligibility decisions.
/// </summary>
public sealed class ConfirmRoster(
    ICallerAccess access,
    IHouseholdRepository households,
    IHouseholdRunLock runLock,
    IWaveRepository waves,
    IHouseholdAssignmentService assignmentService,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<RosterConfirmationResponse>> HandleAsync(Guid householdId, CancellationToken cancellationToken)
    {
        var id = HouseholdId.From(householdId);
        if (!await access.CanManageHouseholdAsync(id, cancellationToken))
        {
            return Result.Failure<RosterConfirmationResponse>(HouseholdErrors.HouseholdNotFound);
        }

        return await unitOfWork.ExecuteInTransactionAsync(token => ConfirmAsync(id, token), cancellationToken);
    }

    private async Task<Result<RosterConfirmationResponse>> ConfirmAsync(HouseholdId id, CancellationToken cancellationToken)
    {
        await runLock.LockAsync(id, cancellationToken);
        var household = await households.GetAsync(id, cancellationToken);
        if (household is null)
        {
            return Result.Failure<RosterConfirmationResponse>(HouseholdErrors.HouseholdNotFound);
        }

        var now = timeProvider.GetUtcNow();
        var confirmed = household.ConfirmRoster(now);
        if (confirmed.IsFailure)
        {
            return Result.Failure<RosterConfirmationResponse>(confirmed.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "households.roster.confirmed", "household", id.Value, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<RosterConfirmationResponse>(committed.Error!);
        }

        var open = await waves.ListOpenAtAsync(now, cancellationToken);
        if (open.Count > 1)
        {
            return Result.Failure<RosterConfirmationResponse>(AssignmentErrors.WaveAmbiguous);
        }

        if (open.Count == 0)
        {
            return Result.Success(new RosterConfirmationResponse(id.Value, now, null, null, []));
        }

        var wave = open[0];
        var run = await assignmentService.AssignAsync(id, wave.Id, cancellationToken);
        if (run.IsFailure)
        {
            return Result.Failure<RosterConfirmationResponse>(run.Error!);
        }

        var live = run.Value.Created.Concat(run.Value.Kept)
            .Select(assignment => new RosterAssignmentResponse(
                assignment.AssignmentId, assignment.MemberId, assignment.InstrumentCode, assignment.SectionCode, assignment.Status))
            .OrderBy(assignment => assignment.MemberId)
            .ThenBy(assignment => assignment.InstrumentCode, StringComparer.Ordinal)
            .ThenBy(assignment => assignment.SectionCode, StringComparer.Ordinal)
            .ToList();
        return Result.Success(new RosterConfirmationResponse(id.Value, now, wave.Id.Value, wave.Code, live));
    }
}
