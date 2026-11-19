using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;

namespace FamilyResilience.Application.Scoring;

/// <summary>
/// The family's resilience over time (D-132): the household assessment's recorded totals for the members the caller may
/// act for, on the approved scale. Only the household's head gets them (D-103); anyone else is told the chart is the
/// head's. Every read is audited, like reading a result.
/// </summary>
public sealed class GetResilienceTrend(
    ICallerAccess access,
    IParticipantAccess participant,
    IResilienceTrendReadModel checkIns,
    ScoreScaleReader scales,
    IAuditLog auditLog)
{
    public async Task<Result<ResilienceTrendResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var trend = await participant.IsHeadOfHouseholdAsync(cancellationToken)
            ? await TrendAsync(cancellationToken)
            : ResilienceTrend.HeadOfHouseholdOnly;

        var recorded = await auditLog.RecordAsync(
            new AuditRecord("scoring.resilience_trend.read", "resilience_trend", null, AuditOutcome.Succeeded), cancellationToken);
        return recorded.IsFailure ? Result.Failure<ResilienceTrendResponse>(recorded.Error!) : Result.Success(trend);
    }

    private async Task<ResilienceTrendResponse> TrendAsync(CancellationToken cancellationToken)
    {
        var members = await access.ActableMemberIdsAsync(cancellationToken);
        var found = members.Count == 0
            ? []
            : await checkIns.ListCheckInsAsync(members, ResilienceTrend.MaxCheckIns, cancellationToken);

        var scaleByRuleSet = new Dictionary<RuleSetId, ScoreScale>();
        foreach (var ruleSetId in found.Select(checkIn => checkIn.RuleSetId).OfType<RuleSetId>().Distinct())
        {
            if (await scales.ReadAsync(ruleSetId, cancellationToken) is { } scale)
            {
                scaleByRuleSet[ruleSetId] = scale;
            }
        }

        return ResilienceTrend.Build(found, scaleByRuleSet);
    }
}
