using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Members;
using FamilyResilience.Domain.Questionnaires;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Domain.Referrals;

/// <summary>
/// A consented hand-off to support, decided by an approved referral rule set. The referral records the consent that
/// authorised it. No partner integration exists yet: an assigned service is a code, not a claim of delivery.
/// </summary>
public sealed class SupportReferral : AggregateRoot<SupportReferralId>
{
    public const int MaxActorIdLength = 200;

    private SupportReferral(SupportReferralId id)
        : base(id)
    {
    }

    public AssessmentSessionId AssessmentSessionId { get; private set; }

    public FamilyMemberId FamilyMemberId { get; private set; }

    /// <summary>Copied from the session, so the database can prove the referral rule set belongs to the same version.</summary>
    public InstrumentVersionId InstrumentVersionId { get; private set; }

    public string CategoryCode { get; private set; } = null!;

    public RuleSetId ReferralRuleSetId { get; private set; }

    public ConsentRecordId ConsentRecordId { get; private set; }

    public ReferralStatus Status { get; private set; }

    public string? AssignedServiceCode { get; private set; }

    /// <summary>The partner a support coordinator handed the referral to, if any.</summary>
    public PartnerOrganisationId? PartnerOrganisationId { get; private set; }

    public HandOffMethod? HandOffMethod { get; private set; }

    public DateTimeOffset? HandedOffAt { get; private set; }

    /// <summary>The token subject of the support coordinator who recorded the hand-off.</summary>
    public string? HandedOffBy { get; private set; }

    public string? ResolutionCode { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? ResolvedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Result<SupportReferral> Create(
        AssessmentSession session, RuleSet referralRules, ConsentRecord consent, string categoryCode, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(referralRules);
        ArgumentNullException.ThrowIfNull(consent);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<SupportReferral>(CommonErrors.TimeNotUtc);
        }

        if (session.Status != SessionStatus.Scored)
        {
            return Result.Failure<SupportReferral>(ReferralErrors.SessionNotScored);
        }

        if (referralRules.Kind != RuleSetKind.Referral)
        {
            return Result.Failure<SupportReferral>(ReferralErrors.RuleSetWrongKind);
        }

        if (referralRules.Status != RuleSetStatus.Approved)
        {
            return Result.Failure<SupportReferral>(ReferralErrors.RuleSetNotApproved);
        }

        if (referralRules.InstrumentVersionId != session.InstrumentVersionId)
        {
            return Result.Failure<SupportReferral>(ReferralErrors.RuleSetVersionMismatch);
        }

        if (!consent.IsActive)
        {
            return Result.Failure<SupportReferral>(ReferralErrors.ConsentNotActive);
        }

        if (consent.ConsentType != ConsentType.Referral)
        {
            return Result.Failure<SupportReferral>(ReferralErrors.ConsentWrongType);
        }

        if (consent.FamilyMemberId != session.FamilyMemberId)
        {
            return Result.Failure<SupportReferral>(ReferralErrors.ConsentMemberMismatch);
        }

        if (!Codes.IsSnakeCode(categoryCode))
        {
            return Result.Failure<SupportReferral>(ReferralErrors.CategoryCodeInvalid);
        }

        var referral = new SupportReferral(SupportReferralId.New())
        {
            AssessmentSessionId = session.Id,
            FamilyMemberId = session.FamilyMemberId,
            InstrumentVersionId = session.InstrumentVersionId,
            CategoryCode = categoryCode,
            ReferralRuleSetId = referralRules.Id,
            ConsentRecordId = consent.Id,
            Status = ReferralStatus.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
        referral.Raise(new SupportReferralCreated(referral.Id, session.Id, now));
        return Result.Success(referral);
    }

    public Result AssignService(string serviceCode, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != ReferralStatus.Open)
        {
            return Result.Failure(ReferralErrors.NotOpen);
        }

        if (!Codes.IsSnakeCode(serviceCode))
        {
            return Result.Failure(ReferralErrors.ServiceCodeInvalid);
        }

        AssignedServiceCode = serviceCode;
        return ChangeStatus(ReferralStatus.InProgress, now);
    }

    /// <summary>
    /// Records that a support coordinator handed the open referral to <paramref name="partner"/> outside the platform
    /// (<see cref="Referrals.HandOffMethod.Manual"/>). The partner must be active and cover the referral's category; the
    /// partner's code becomes the assigned service. The caller must hold the referral consent under a share lock.
    /// </summary>
    public Result HandOff(PartnerOrganisation partner, string handedOffBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentException.ThrowIfNullOrWhiteSpace(handedOffBy);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status != ReferralStatus.Open)
        {
            return Result.Failure(ReferralErrors.NotOpen);
        }

        if (!partner.IsActive)
        {
            return Result.Failure(ReferralErrors.PartnerInactive);
        }

        if (!partner.Covers(CategoryCode))
        {
            return Result.Failure(ReferralErrors.PartnerCategoryMismatch);
        }

        if (handedOffBy.Length > MaxActorIdLength)
        {
            return Result.Failure(ReferralErrors.ActorIdTooLong);
        }

        AssignedServiceCode = partner.Code;
        PartnerOrganisationId = partner.Id;
        HandOffMethod = Referrals.HandOffMethod.Manual;
        HandedOffAt = now;
        HandedOffBy = handedOffBy;
        return ChangeStatus(ReferralStatus.InProgress, now);
    }

    public Result Resolve(string resolutionCode, string? resolvedBy, DateTimeOffset now) =>
        Close(ReferralStatus.Resolved, resolutionCode, resolvedBy, now);

    public Result Cancel(string resolutionCode, string? resolvedBy, DateTimeOffset now) =>
        Close(ReferralStatus.Cancelled, resolutionCode, resolvedBy, now);

    private Result Close(ReferralStatus status, string resolutionCode, string? resolvedBy, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (Status is ReferralStatus.Resolved or ReferralStatus.Cancelled)
        {
            return Result.Failure(ReferralErrors.AlreadyClosed);
        }

        if (!Codes.IsSnakeCode(resolutionCode))
        {
            return Result.Failure(ReferralErrors.ResolutionCodeInvalid);
        }

        if (resolvedBy is { Length: > MaxActorIdLength })
        {
            return Result.Failure(ReferralErrors.ActorIdTooLong);
        }

        ResolutionCode = resolutionCode;
        ResolvedBy = resolvedBy;
        ResolvedAt = now;
        return ChangeStatus(status, now);
    }

    private Result ChangeStatus(ReferralStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
        Raise(new SupportReferralStatusChanged(Id, status, now));
        return Result.Success();
    }
}
