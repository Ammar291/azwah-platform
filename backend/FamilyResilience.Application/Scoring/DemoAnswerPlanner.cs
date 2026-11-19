using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// DEMONSTRATIONS ONLY (D-130). Answers for a session that the approved scoring rule set for its scope scores in the
/// asked-for band (<see cref="DemoAnswerPlan"/>). It uses only an approved rule set, as scoring does, and checks its own
/// plan with the same strategy before returning it. It records nothing: the answers still have to be saved and the
/// session submitted, and the result is then scored like any other. A checked plan is kept in
/// <see cref="DemoAnswerPlanCache"/> for its approved rule set, so later taps do no planning (D-143).
/// </summary>
public sealed class DemoAnswerPlanner(
    IInstrumentVersionRepository versions,
    IRuleSetRepository ruleSets,
    ScoringStrategyCatalog catalog,
    DemoAnswerPlanCache plans)
{
    public async Task<Result<DemoAnswerPlan>> PlanAsync(AssessmentSession session, DemoScoreLevel level, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var version = await versions.GetAsync(session.InstrumentVersionId, cancellationToken)
            ?? throw new InvalidOperationException("A session's version must exist.");
        var sectionCode = ApprovedScoringPolicy.SectionCodeOf(session, version);
        var policy = await ApprovedScoringPolicy.FindAsync(ruleSets, version, sectionCode, cancellationToken);
        if (policy.IsFailure)
        {
            return Result.Failure<DemoAnswerPlan>(policy.Error!);
        }

        var ruleSetId = policy.Value.RuleSet.Id;
        if (plans.TryGet(ruleSetId, sectionCode, level, out var kept))
        {
            return Result.Success(kept);
        }

        var definition = policy.Value.Definition;
        var strategy = catalog.Resolve(definition.PolicyCode, definition.AlgorithmVersion);
        if (strategy.IsFailure)
        {
            return Result.Failure<DemoAnswerPlan>(strategy.Error!);
        }

        var content = ScoringContent.From(version, sectionCode);
        if (content.IsFailure)
        {
            return Result.Failure<DemoAnswerPlan>(content.Error!);
        }

        var parameters = strategy.Value.ValidateParameters(definition.Parameters, content.Value);
        if (parameters.IsFailure)
        {
            return Result.Failure<DemoAnswerPlan>(parameters.Error!);
        }

        if (parameters.Value is not SummedItemValuesParameters summed)
        {
            return Result.Failure<DemoAnswerPlan>(ScoringErrors.PolicyUnsupported);
        }

        var plan = DemoAnswerPlan.Create(content.Value, summed, level);
        var check = strategy.Value.Score(new ScoringInput(content.Value, summed, plan.Answers));
        if (check.IsFailure
            || check.Value.Status != ScoreStatus.Complete
            || (summed.Total && check.Value.TotalScore != plan.Total)
            || check.Value.TierCode != plan.TierCode)
        {
            throw new InvalidOperationException("A demonstration plan must score as planned.");
        }

        plans.Keep(ruleSetId, sectionCode, level, plan);
        return Result.Success(plan);
    }
}
