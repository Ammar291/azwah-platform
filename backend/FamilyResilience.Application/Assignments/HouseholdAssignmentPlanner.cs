using FamilyResilience.Application.Households;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Assignments;
using FamilyResilience.Domain.Assignments.Eligibility;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Assignments;

/// <summary>
/// Loads what an assignment run needs and plans it without changing anything: the household and wave, every approved
/// eligibility rule set of every active instrument's effective version, a decision per member and rule, and the
/// assignments already in the wave. Ages are taken on the evaluation date.
/// </summary>
public sealed class HouseholdAssignmentPlanner(
    IHouseholdRepository households,
    IWaveRepository waves,
    IInstrumentRepository instruments,
    IInstrumentVersionRepository versions,
    IRuleSetRepository ruleSets,
    IAssessmentAssignmentRepository assignments)
{
    public async Task<Result<PreparedAssignmentRun>> PrepareAsync(
        Guid householdId, Guid waveId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var household = await households.GetAsync(HouseholdId.From(householdId), cancellationToken);
        if (household is null)
        {
            return Result.Failure<PreparedAssignmentRun>(HouseholdErrors.HouseholdNotFound);
        }

        // Open question 25: until the domain decides it, a withdrawn household gets no new assignments.
        if (household.Status != HouseholdStatus.Active)
        {
            return Result.Failure<PreparedAssignmentRun>(HouseholdErrors.NotActive);
        }

        var wave = await waves.GetAsync(WaveId.From(waveId), cancellationToken);
        if (wave is null)
        {
            return Result.Failure<PreparedAssignmentRun>(AssignmentErrors.WaveNotFound);
        }

        if (!wave.AcceptsAssignments || wave.ClosesAt <= now)
        {
            return Result.Failure<PreparedAssignmentRun>(AssignmentErrors.WaveClosed);
        }

        var targets = await TargetsAsync(now, cancellationToken);
        if (targets.IsFailure)
        {
            return Result.Failure<PreparedAssignmentRun>(targets.Error!);
        }

        var asOf = DateOnly.FromDateTime(now.UtcDateTime);
        var decisions = household.Members
            .OrderBy(member => member.Id.Value)
            .SelectMany(member => targets.Value.Select(target =>
                EligibilityEvaluator.Evaluate(target.Rule, EligibilityFacts.From(household, member, asOf), now)))
            .ToList();
        var existing = await ExistingAsync(household, wave.Id, cancellationToken);
        return Result.Success(new PreparedAssignmentRun(
            household, wave, targets.Value, decisions, AssignmentPlanner.Plan(decisions, existing), now));
    }

    private async Task<Result<IReadOnlyList<AssignmentTarget>>> TargetsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var targets = new List<AssignmentTarget>();
        foreach (var instrument in await instruments.ListActiveAsync(cancellationToken))
        {
            var version = await versions.GetEffectiveApprovedAsync(instrument.Id, now, cancellationToken);
            if (version is null)
            {
                continue;
            }

            foreach (var ruleSet in await ruleSets.ListApprovedAsync(RuleSetKind.Eligibility, version.Id, cancellationToken))
            {
                var rule = EligibilityRule.From(ruleSet, instrument, version);
                if (rule.IsFailure)
                {
                    return Result.Failure<IReadOnlyList<AssignmentTarget>>(rule.Error!);
                }

                targets.Add(new AssignmentTarget(instrument, version, ruleSet, rule.Value));
            }
        }

        return targets.GroupBy(target => (target.Rule.InstrumentCode, target.Rule.SectionCode)).Any(group => group.Count() > 1)
            ? Result.Failure<IReadOnlyList<AssignmentTarget>>(EligibilityErrors.AmbiguousRules)
            : Result.Success<IReadOnlyList<AssignmentTarget>>(
                targets.OrderBy(target => target.Rule.InstrumentCode, StringComparer.Ordinal)
                    .ThenBy(target => target.Rule.SectionCode, StringComparer.Ordinal).ToList());
    }

    private async Task<IReadOnlyList<ExistingAssignment>> ExistingAsync(Household household, WaveId waveId, CancellationToken cancellationToken)
    {
        var owners = new Dictionary<InstrumentVersionId, (string InstrumentCode, InstrumentVersion Version)>();
        var existing = new List<ExistingAssignment>();
        foreach (var member in household.Members.OrderBy(member => member.Id.Value))
        {
            foreach (var assignment in await assignments.ListForMemberAsync(member.Id, waveId, cancellationToken))
            {
                if (!owners.TryGetValue(assignment.InstrumentVersionId, out var owner))
                {
                    var version = await versions.GetAsync(assignment.InstrumentVersionId, cancellationToken)
                        ?? throw new InvalidOperationException("An assignment's version must exist.");
                    var instrument = await instruments.GetAsync(version.InstrumentId, cancellationToken)
                        ?? throw new InvalidOperationException("A version's instrument must exist.");
                    owner = (instrument.Code, version);
                    owners[assignment.InstrumentVersionId] = owner;
                }

                var sectionCode = assignment.InstrumentSectionId is { } sectionId
                    ? (owner.Version.FindSection(sectionId)
                        ?? throw new InvalidOperationException("An assignment's section must exist in its version.")).Code
                    : null;
                existing.Add(new ExistingAssignment(assignment, owner.InstrumentCode, sectionCode));
            }
        }

        return existing;
    }
}
