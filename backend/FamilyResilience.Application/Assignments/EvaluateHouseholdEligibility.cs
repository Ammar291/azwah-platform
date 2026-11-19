using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Application.Assignments;

/// <summary>A dry run: the decisions and what an assignment run would change. Writes only an audit event.</summary>
public sealed class EvaluateHouseholdEligibility(HouseholdAssignmentPlanner planner, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<EligibilityEvaluationResponse>> HandleAsync(
        Guid householdId, HouseholdWaveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prepared = await planner.PrepareAsync(householdId, request.WaveId, timeProvider.GetUtcNow(), cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<EligibilityEvaluationResponse>(prepared.Error!);
        }

        var recorded = await auditLog.RecordAsync(
            new AuditRecord("assignments.eligibility.evaluated", "household", householdId.ToString(), AuditOutcome.Succeeded),
            cancellationToken);
        if (recorded.IsFailure)
        {
            return Result.Failure<EligibilityEvaluationResponse>(recorded.Error!);
        }

        var run = prepared.Value;
        return Result.Success(new EligibilityEvaluationResponse(
            householdId,
            request.WaveId,
            run.EvaluatedAt,
            run.Decisions.Select(decision => decision.ToResponse()).ToList(),
            run.Plan.Create.Select(planned => new PlannedAssignmentResponse(
                planned.Decision.MemberId.Value, planned.Decision.InstrumentCode, planned.Decision.SectionCode,
                planned.Decision.RuleSetId.Value, planned.Reason)).ToList(),
            run.Plan.Cancel.Select(existing => existing.ToSummary()).ToList(),
            run.Plan.Keep.Select(existing => existing.ToSummary()).ToList()));
    }
}
