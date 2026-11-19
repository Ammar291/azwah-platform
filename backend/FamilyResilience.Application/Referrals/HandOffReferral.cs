using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Consent;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Referrals;

namespace FamilyResilience.Application.Referrals;

/// <summary>
/// Records that the signed-in support coordinator handed an open referral to a partner organisation outside the
/// platform. Nothing is sent to the partner. The member's referral consent and the partner are read under share locks,
/// so a concurrent withdrawal or deactivation waits, and the hand-off never commits against a withdrawn consent or an
/// inactive partner; the partner must also cover the referral's category. Audited as
/// <c>referrals.referral.handed_off</c>.
/// </summary>
public sealed class HandOffReferral(
    ISupportReferralRepository referrals,
    IPartnerOrganisationRepository partners,
    IConsentRecordRepository consents,
    IReferralCoordinationReadModel queue,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public Task<Result<CoordinatedReferralResponse>> HandleAsync(Guid referralId, HandOffRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var partnerId = request.PartnerId ?? throw new ArgumentException("A partner is required.", nameof(request));

        return unitOfWork.ExecuteInTransactionAsync(token => HandOffAsync(referralId, partnerId, token), cancellationToken);
    }

    private async Task<Result<CoordinatedReferralResponse>> HandOffAsync(Guid referralId, Guid partnerId, CancellationToken cancellationToken)
    {
        var id = SupportReferralId.From(referralId);
        var referral = await referrals.GetAsync(id, cancellationToken);
        if (referral is null)
        {
            return Result.Failure<CoordinatedReferralResponse>(ReferralErrors.ReferralNotFound);
        }

        if (referral.Status != ReferralStatus.Open)
        {
            return Result.Failure<CoordinatedReferralResponse>(ReferralErrors.NotOpen);
        }

        var consent = await consents.GetActiveWithShareLockAsync(referral.FamilyMemberId, ConsentType.Referral, cancellationToken);
        if (consent is null || consent.Id != referral.ConsentRecordId)
        {
            return Result.Failure<CoordinatedReferralResponse>(ReferralErrors.ConsentNotActive);
        }

        // Locked too, so a deactivation in flight is seen as inactive (409) instead of tripping the database guard (500).
        var partner = await partners.GetWithShareLockAsync(PartnerOrganisationId.From(partnerId), cancellationToken);
        if (partner is null)
        {
            return Result.Failure<CoordinatedReferralResponse>(ReferralErrors.PartnerNotFound);
        }

        var coordinator = currentUser.SubjectId ?? throw new InvalidOperationException("A hand-off needs a signed-in coordinator.");
        var handedOff = referral.HandOff(partner, coordinator, timeProvider.GetUtcNow());
        if (handedOff.IsFailure)
        {
            return Result.Failure<CoordinatedReferralResponse>(handedOff.Error!);
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "referrals.referral.handed_off", "support_referral", referralId, cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure<CoordinatedReferralResponse>(committed.Error!);
        }

        return Result.Success(await queue.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("A referral that was just handed off must be readable."));
    }
}
