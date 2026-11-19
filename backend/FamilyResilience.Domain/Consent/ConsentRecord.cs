using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Consent;

/// <summary>
/// A member's consent for one purpose against an approved policy version. Withdrawal is a state change, never a
/// delete. At most one active record per member and type (enforced by a partial unique index).
/// </summary>
public sealed class ConsentRecord : AggregateRoot<ConsentRecordId>
{
    public const int MaxActorIdLength = 200;

    private ConsentRecord(ConsentRecordId id)
        : base(id)
    {
    }

    public FamilyMemberId FamilyMemberId { get; private set; }

    public HouseholdId HouseholdId { get; private set; }

    public ConsentType ConsentType { get; private set; }

    public ConsentPolicyVersionId ConsentPolicyVersionId { get; private set; }

    public ConsentCaptureMethod CaptureMethod { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>The token subject of whoever recorded the grant; null when the platform recorded it.</summary>
    public string? GrantedBy { get; private set; }

    public DateTimeOffset? WithdrawnAt { get; private set; }

    public string? WithdrawnBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => WithdrawnAt is null;

    public static Result<ConsentRecord> Grant(
        FamilyMember member,
        ConsentPolicyVersion policy,
        ConsentCaptureMethod captureMethod,
        string? grantedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(policy);

        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure<ConsentRecord>(CommonErrors.TimeNotUtc);
        }

        if (!member.IsActive)
        {
            return Result.Failure<ConsentRecord>(ConsentErrors.MemberNotActive);
        }

        if (policy.Status != ConsentPolicyStatus.Approved)
        {
            return Result.Failure<ConsentRecord>(ConsentErrors.PolicyNotApproved);
        }

        if (!Enum.IsDefined(captureMethod))
        {
            return Result.Failure<ConsentRecord>(ConsentErrors.CaptureMethodInvalid);
        }

        if (grantedBy is { Length: > MaxActorIdLength })
        {
            return Result.Failure<ConsentRecord>(ConsentErrors.ActorIdTooLong);
        }

        var consent = new ConsentRecord(ConsentRecordId.New())
        {
            FamilyMemberId = member.Id,
            HouseholdId = member.HouseholdId,
            ConsentType = policy.ConsentType,
            ConsentPolicyVersionId = policy.Id,
            CaptureMethod = captureMethod,
            GrantedAt = now,
            GrantedBy = grantedBy,
            UpdatedAt = now,
        };
        consent.Raise(new ConsentGranted(consent.Id, member.Id, policy.ConsentType, now));
        return Result.Success(consent);
    }

    public Result Withdraw(string? withdrawnBy, DateTimeOffset now)
    {
        if (!UtcTime.IsUtc(now))
        {
            return Result.Failure(CommonErrors.TimeNotUtc);
        }

        if (!IsActive)
        {
            return Result.Failure(ConsentErrors.NotActive);
        }

        if (withdrawnBy is { Length: > MaxActorIdLength })
        {
            return Result.Failure(ConsentErrors.ActorIdTooLong);
        }

        if (now < GrantedAt)
        {
            return Result.Failure(ConsentErrors.WithdrawnBeforeGranted);
        }

        WithdrawnAt = now;
        WithdrawnBy = withdrawnBy;
        UpdatedAt = now;
        Raise(new ConsentWithdrawn(Id, FamilyMemberId, ConsentType, now));
        return Result.Success();
    }
}
