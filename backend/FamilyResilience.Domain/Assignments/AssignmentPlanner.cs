using FamilyResilience.Domain.Assignments.Eligibility;

namespace FamilyResilience.Domain.Assignments;

/// <summary>
/// Decides what an assignment run changes, from the eligibility decisions and the assignments already in the wave.
/// Assignments match on member, instrument code and module code, so a newer version of the same module never
/// duplicates one that is live or completed. Started and completed assignments are never cancelled; nothing is edited.
/// </summary>
public static class AssignmentPlanner
{
    public static AssignmentPlan Plan(IReadOnlyList<EligibilityDecision> decisions, IReadOnlyList<ExistingAssignment> existing)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(existing);

        var create = new List<AssignmentToCreate>();
        var cancel = new List<ExistingAssignment>();
        var keep = new List<ExistingAssignment>();
        foreach (var decision in decisions)
        {
            var same = existing
                .Where(candidate => candidate.Assignment.FamilyMemberId == decision.MemberId
                    && candidate.InstrumentCode == decision.InstrumentCode
                    && candidate.SectionCode == decision.SectionCode)
                .ToList();
            var live = same
                .Where(candidate => candidate.Assignment.Status
                    is AssignmentStatus.Assigned or AssignmentStatus.InProgress or AssignmentStatus.Completed)
                .ToList();

            if (decision.Outcome == EligibilityOutcome.Eligible && live.Count == 0)
            {
                var replacesFinished = same.Exists(candidate => candidate.Assignment.Status
                    is AssignmentStatus.Cancelled or AssignmentStatus.Expired);
                create.Add(new AssignmentToCreate(
                    decision, replacesFinished ? AssignmentReason.Reissue : AssignmentReason.EligibilityRule));
            }
            else if (decision.Outcome == EligibilityOutcome.Ineligible)
            {
                cancel.AddRange(live.Where(candidate => candidate.Assignment.Status == AssignmentStatus.Assigned));
                keep.AddRange(live.Where(candidate => candidate.Assignment.Status != AssignmentStatus.Assigned));
            }
            else
            {
                keep.AddRange(live);
            }
        }

        return new AssignmentPlan(create, cancel, keep);
    }
}
