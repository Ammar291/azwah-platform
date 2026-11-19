using FamilyResilience.Application.Abstractions;
using FamilyResilience.Application.Audit;
using FamilyResilience.Application.Households;
using FamilyResilience.Application.Identity;
using FamilyResilience.Application.Referrals;
using FamilyResilience.Domain.Audit;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Consent;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Consent;

/// <summary>
/// Records a member's grant or withdrawal, for a member the caller may act for (themselves, or as manager a member
/// without an account of their own; D-49). Granting needs an active household, an adult member and the current
/// approved policy; granting a newer version withdraws the older one first. Withdrawing referral consent cancels the
/// referrals that relied on it, after the withdrawal has committed (D-45). Every change is audited.
/// </summary>
public sealed class RecordConsentDecision(
    ICallerAccess access,
    ICurrentUser currentUser,
    IHouseholdRepository households,
    IConsentPolicyVersionRepository policies,
    IConsentRecordRepository consents,
    ISupportReferralRepository referrals,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const string ReferralCancelledResolution = "consent_withdrawn";

    public async Task<Result<ConsentRecordResponse>> HandleAsync(RecordConsentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var memberId = FamilyMemberId.From(request.MemberId);
        var caller = await access.GetCallerAsync(cancellationToken);
        if (caller?.Household is not { } snapshot || !await access.CanActForMemberAsync(memberId, cancellationToken))
        {
            return Result.Failure<ConsentRecordResponse>(HouseholdErrors.MemberNotFound);
        }

        var policy = await policies.GetAsync(ConsentPolicyVersionId.From(request.PolicyVersionId), cancellationToken);
        if (policy is null)
        {
            return Result.Failure<ConsentRecordResponse>(ConsentErrors.PolicyNotFound);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            token => request.Decision == ConsentDecision.Withdraw
                ? WithdrawAsync(memberId, policy, token)
                : GrantAsync(snapshot.Id, memberId, policy, token),
            cancellationToken);
    }

    private async Task<Result<ConsentRecordResponse>> GrantAsync(
        HouseholdId householdId, FamilyMemberId memberId, ConsentPolicyVersion policy, CancellationToken cancellationToken)
    {
        var household = await households.GetAsync(householdId, cancellationToken)
            ?? throw new InvalidOperationException("The caller's household must exist.");
        var member = household.FindMember(memberId) ?? throw new InvalidOperationException("An actable member belongs to the household.");
        var now = timeProvider.GetUtcNow();

        // Open question 25: until the domain decides it, a withdrawn household grants nothing new.
        if (household.Status != HouseholdStatus.Active)
        {
            return Result.Failure<ConsentRecordResponse>(HouseholdErrors.NotActive);
        }

        if (!member.IsCertainlyAdultOn(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            return Result.Failure<ConsentRecordResponse>(ConsentErrors.MemberUnder18);
        }

        if (policy.Status != ConsentPolicyStatus.Approved)
        {
            return Result.Failure<ConsentRecordResponse>(ConsentErrors.PolicyNotApproved);
        }

        if ((await policies.ListCurrentAsync(cancellationToken)).All(current => current.Id != policy.Id))
        {
            return Result.Failure<ConsentRecordResponse>(ConsentErrors.PolicyNotCurrent);
        }

        var existing = await consents.GetActiveAsync(memberId, policy.ConsentType, cancellationToken);
        if (existing?.ConsentPolicyVersionId == policy.Id)
        {
            return Result.Success(existing.ToResponse(policy.VersionNumber));
        }

        if (existing is not null)
        {
            // One active consent per member and type: the older version is withdrawn and committed before the new grant.
            var superseded = await WithdrawCoreAsync(existing, now, cancellationToken);
            if (superseded.IsFailure)
            {
                return Result.Failure<ConsentRecordResponse>(superseded.Error!);
            }
        }

        var granted = ConsentRecord.Grant(member, policy, ConsentCaptureMethod.InApp, currentUser.SubjectId, now);
        if (granted.IsFailure)
        {
            return Result.Failure<ConsentRecordResponse>(granted.Error!);
        }

        consents.Add(granted.Value);
        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "consent.consent.granted", "consent_record", granted.Value.Id.Value, cancellationToken);
        return committed.IsFailure
            ? Result.Failure<ConsentRecordResponse>(committed.Error!)
            : Result.Success(granted.Value.ToResponse(policy.VersionNumber));
    }

    private async Task<Result<ConsentRecordResponse>> WithdrawAsync(
        FamilyMemberId memberId, ConsentPolicyVersion policy, CancellationToken cancellationToken)
    {
        var existing = await consents.GetActiveAsync(memberId, policy.ConsentType, cancellationToken);
        if (existing is null)
        {
            return Result.Failure<ConsentRecordResponse>(ConsentErrors.NotActive);
        }

        var withdrawn = await WithdrawCoreAsync(existing, timeProvider.GetUtcNow(), cancellationToken);
        if (withdrawn.IsFailure)
        {
            return Result.Failure<ConsentRecordResponse>(withdrawn.Error!);
        }

        var version = existing.ConsentPolicyVersionId == policy.Id
            ? policy.VersionNumber
            : (await policies.GetAsync(existing.ConsentPolicyVersionId, cancellationToken))?.VersionNumber
              ?? throw new InvalidOperationException("A consent record's policy must exist.");
        return Result.Success(existing.ToResponse(version));
    }

    /// <summary>Withdraws and commits, then cancels the referrals that relied on the consent and commits again.</summary>
    private async Task<Result> WithdrawCoreAsync(ConsentRecord consent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var withdrawn = consent.Withdraw(currentUser.SubjectId, now);
        if (withdrawn.IsFailure)
        {
            return withdrawn;
        }

        var committed = await AuditedCommit.RunAsync(
            auditLog, unitOfWork, "consent.consent.withdrawn", "consent_record", consent.Id.Value, cancellationToken);
        if (committed.IsFailure || consent.ConsentType != ConsentType.Referral)
        {
            return committed;
        }

        var live = await referrals.ListLiveForConsentAsync(consent.Id, cancellationToken);
        if (live.Count == 0)
        {
            return Result.Success();
        }

        foreach (var referral in live)
        {
            var cancelled = referral.Cancel(ReferralCancelledResolution, currentUser.SubjectId, now);
            if (cancelled.IsFailure)
            {
                return cancelled;
            }

            var staged = auditLog.Stage(new AuditRecord(
                "referrals.referral.cancelled", "support_referral", referral.Id.Value.ToString(), AuditOutcome.Succeeded));
            if (staged.IsFailure)
            {
                return staged;
            }
        }

        return await unitOfWork.CommitAsync(cancellationToken);
    }
}
