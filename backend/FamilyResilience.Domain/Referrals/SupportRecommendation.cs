using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Scoring;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Domain.Referrals;

/// <summary>
/// A routing decision: an approved referral rule set routed a scored session to a support category. It records the
/// score result it was decided from and, when the member's referral consent was active, the referral it produced.
/// Without that consent it stays a recommendation the participant can see; nothing is shared. Never changed.
/// </summary>
public sealed class SupportRecommendation : AggregateRoot<SupportRecommendationId>
{
    private SupportRecommendation(SupportRecommendationId id)
        : base(id)
    {
    }

    public AssessmentSessionId AssessmentSessionId { get; private set; }

    public FamilyMemberId FamilyMemberId { get; private set; }

    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public ScoreResultId ScoreResultId { get; private set; }

    public RuleSetId ReferralRuleSetId { get; private set; }

    public string CategoryCode { get; private set; } = null!;

    public SupportReferralId? SupportReferralId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<SupportRecommendation> Record(
        AssessmentSession session,
        ScoreResult score,
        RuleSet referralRules,
        string categoryCode,
        SupportReferral? referral,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(referralRules);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<SupportRecommendation>(CommonErrors.TimeNotUtc);
        }

        if (session.Status != SessionStatus.Scored)
        {
            return Result.Failure<SupportRecommendation>(ReferralErrors.SessionNotScored);
        }

        if (score.AssessmentSessionId != session.Id)
        {
            return Result.Failure<SupportRecommendation>(ReferralErrors.ScoreMismatch);
        }

        if (referralRules.Kind != RuleSetKind.Referral)
        {
            return Result.Failure<SupportRecommendation>(ReferralErrors.RuleSetWrongKind);
        }

        if (referralRules.Status != RuleSetStatus.Approved)
        {
            return Result.Failure<SupportRecommendation>(ReferralErrors.RuleSetNotApproved);
        }

        if (referralRules.InstrumentVersionId != session.InstrumentVersionId)
        {
            return Result.Failure<SupportRecommendation>(ReferralErrors.RuleSetVersionMismatch);
        }

        if (!Codes.IsSnakeCode(categoryCode))
        {
            return Result.Failure<SupportRecommendation>(ReferralErrors.CategoryCodeInvalid);
        }

        if (referral is not null && (referral.AssessmentSessionId != session.Id || referral.CategoryCode != categoryCode))
        {
            return Result.Failure<SupportRecommendation>(ReferralErrors.ReferralMismatch);
        }

        return Result.Success(new SupportRecommendation(SupportRecommendationId.New())
        {
            AssessmentSessionId = session.Id,
            FamilyMemberId = session.FamilyMemberId,
            InstrumentVersionId = session.InstrumentVersionId,
            ScoreResultId = score.Id,
            ReferralRuleSetId = referralRules.Id,
            CategoryCode = categoryCode,
            SupportReferralId = referral?.Id,
            CreatedAt = now,
        });
    }
}
