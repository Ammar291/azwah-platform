using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// Evaluates every member against every approved eligibility rule in effect and applies the plan: new assignments
/// (expiring at wave close) and cancellation of not-started ones whose member no longer qualifies. Runs again safely.
/// Runs for one household are serialised by <see cref="IHouseholdRunLock"/>, so concurrent duplicates both succeed and
/// the later one creates nothing; the live-assignment unique index remains only as a backstop.
/// </summary>
public sealed class HouseholdAssignmentService(
    HouseholdAssignmentPlanner planner,
    IAssessmentAssignmentRepository assignments,
    IHouseholdRunLock runLock,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider) : IHouseholdAssignmentService
{
    public async Task<Result<AssignmentRunResponse>> AssignAsync(HouseholdId householdId, WaveId waveId, CancellationToken cancellationToken)
    {
        // First database action: later runs for this household wait here, then plan against what this one committed.
        await runLock.LockAsync(householdId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var prepared = await planner.PrepareAsync(householdId.Value, waveId.Value, now, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<AssignmentRunResponse>(prepared.Error!);
        }

        var run = prepared.Value;
        var created = new List<AssignmentSummaryResponse>();
        foreach (var planned in run.Plan.Create)
        {
            var target = run.TargetFor(planned.Decision);
            var member = run.Household.FindMember(planned.Decision.MemberId)
                ?? throw new InvalidOperationException("A decision names a member of the household.");
            var assigned = AssessmentAssignment.Assign(
                member, target.Version, target.Rule.SectionId, run.Wave, target.RuleSet, planned.Reason, run.Wave.ClosesAt, now);
            if (assigned.IsFailure)
            {
                return Result.Failure<AssignmentRunResponse>(assigned.Error!);
            }

            assignments.Add(assigned.Value);
            var staged = auditLog.Stage(new AuditRecord(
                "assignments.assignment.created", "assessment_assignment", assigned.Value.Id.Value.ToString(), AuditOutcome.Succeeded));
            if (staged.IsFailure)
            {
                return Result.Failure<AssignmentRunResponse>(staged.Error!);
            }

            created.Add(AssignmentResponses.Summary(assigned.Value, target.Instrument.Code, target.Rule.SectionCode));
        }

        var cancelled = new List<AssignmentSummaryResponse>();
        foreach (var existing in run.Plan.Cancel)
        {
            var cancel = existing.Assignment.Cancel(now);
            if (cancel.IsFailure)
            {
                return Result.Failure<AssignmentRunResponse>(cancel.Error!);
            }

            var staged = auditLog.Stage(new AuditRecord(
                "assignments.assignment.cancelled", "assessment_assignment", existing.Assignment.Id.Value.ToString(), AuditOutcome.Succeeded));
            if (staged.IsFailure)
            {
                return Result.Failure<AssignmentRunResponse>(staged.Error!);
            }

            cancelled.Add(existing.ToSummary());
        }

        // Every run reads per-member reason codes, so it is audited as an eligibility read, even a no-op.
        var evaluated = auditLog.Stage(new AuditRecord(
            "assignments.eligibility.evaluated", "household", householdId.Value.ToString(), AuditOutcome.Succeeded));
        if (evaluated.IsFailure)
        {
            return Result.Failure<AssignmentRunResponse>(evaluated.Error!);
        }

        var committed = await unitOfWork.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<AssignmentRunResponse>(committed.Error!);
        }

        return Result.Success(new AssignmentRunResponse(
            householdId.Value,
            waveId.Value,
            now,
            run.Decisions.Select(decision => decision.ToResponse()).ToList(),
            created,
            cancelled,
            run.Plan.Keep.Select(existing => existing.ToSummary()).ToList()));
    }
}
