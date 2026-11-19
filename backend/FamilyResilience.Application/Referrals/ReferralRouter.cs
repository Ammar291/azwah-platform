using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Application.Questionnaires;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Referrals;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// Evaluates the approved referral rule set for a scored session's scope and stages one recommendation per routed
/// category, plus a referral when the member's referral consent is active. The consent is read with a share lock, so a
/// concurrent withdrawal waits. Call it inside <c>IUnitOfWork.ExecuteInTransactionAsync</c> after the score has
/// committed (the database refuses a referral before the session is Scored); the caller commits. No partner
/// integration exists: a referral is a record on the platform, never a claim of hand-off.
/// </summary>
public sealed class ReferralRouter(
    IRuleSetRepository ruleSets,
    IConsentRecordRepository consents,
    ISupportReferralRepository referrals,
    ISupportRecommendationRepository recommendations,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<RoutingOutcome>> RouteAsync(
        AssessmentSession session, ScoreResult score, string? sectionCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(score);

        var rules = await RulesForAsync(session.InstrumentVersionId, sectionCode, cancellationToken);
        if (rules.IsFailure || rules.Value is not { } found)
        {
            return rules.IsFailure ? Result.Failure<RoutingOutcome>(rules.Error!) : Result.Success(RoutingOutcome.None);
        }

        var (ruleSet, definition) = found;
        var alreadyRouted = (await recommendations.ListForSessionAsync(session.Id, cancellationToken))
            .Where(recommendation => recommendation.ScoreResultId == score.Id)
            .Select(recommendation => recommendation.CategoryCode)
            .ToHashSet(StringComparer.Ordinal);
        var categories = definition.CategoriesFor(score).Where(category => !alreadyRouted.Contains(category)).ToList();
        if (categories.Count == 0)
        {
            return Result.Success(RoutingOutcome.None);
        }

        var consent = await consents.GetActiveWithShareLockAsync(session.FamilyMemberId, ConsentType.Referral, cancellationToken);
        var liveCategories = (await referrals.ListForSessionAsync(session.Id, cancellationToken))
            .Where(referral => referral.Status != ReferralStatus.Cancelled)
            .Select(referral => referral.CategoryCode)
            .ToHashSet(StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow();
        var referred = 0;
        foreach (var category in categories)
        {
            SupportReferral? referral = null;
            if (consent is not null && !liveCategories.Contains(category))
            {
                var created = SupportReferral.Create(session, ruleSet, consent, category, now);
                if (created.IsFailure)
                {
                    return Result.Failure<RoutingOutcome>(created.Error!);
                }

                referral = created.Value;
                referrals.Add(referral);
                referred++;
                var stagedReferral = auditLog.Stage(new AuditRecord(
                    "referrals.referral.created", "support_referral", referral.Id.Value.ToString(), AuditOutcome.Succeeded));
                if (stagedReferral.IsFailure)
                {
                    return Result.Failure<RoutingOutcome>(stagedReferral.Error!);
                }
            }

            var recommended = SupportRecommendation.Record(session, score, ruleSet, category, referral, now);
            if (recommended.IsFailure)
            {
                return Result.Failure<RoutingOutcome>(recommended.Error!);
            }

            recommendations.Add(recommended.Value);
            var staged = auditLog.Stage(new AuditRecord(
                "referrals.recommendation.recorded", "support_recommendation", recommended.Value.Id.Value.ToString(), AuditOutcome.Succeeded));
            if (staged.IsFailure)
            {
                return Result.Failure<RoutingOutcome>(staged.Error!);
            }
        }

        return Result.Success(new RoutingOutcome(categories.Count, referred));
    }

    /// <summary>The single approved referral rule set for the scope; none is not an error, several are.</summary>
    private async Task<Result<(RuleSet RuleSet, ReferralDefinition Definition)?>> RulesForAsync(
        InstrumentVersionId versionId, string? sectionCode, CancellationToken cancellationToken)
    {
        var matching = new List<(RuleSet, ReferralDefinition)>();
        foreach (var ruleSet in await ruleSets.ListApprovedAsync(RuleSetKind.Referral, versionId, cancellationToken))
        {
            var definition = ReferralDefinitionParser.Parse(ruleSet.Definition);
            if (definition.IsFailure)
            {
                return Result.Failure<(RuleSet, ReferralDefinition)?>(definition.Error!);
            }

            if (definition.Value.TargetSectionCode == sectionCode)
            {
                matching.Add((ruleSet, definition.Value));
            }
        }

        return matching.Count switch
        {
            0 => Result.Success<(RuleSet, ReferralDefinition)?>(null),
            1 => Result.Success<(RuleSet, ReferralDefinition)?>(matching[0]),
            _ => Result.Failure<(RuleSet, ReferralDefinition)?>(ReferralErrors.AmbiguousRules),
        };
    }
}
