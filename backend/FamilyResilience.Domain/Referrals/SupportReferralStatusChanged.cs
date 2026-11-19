using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Referrals;

[DomainEventType("referrals.support_referral_status_changed.v1")]
public sealed record SupportReferralStatusChanged(
    SupportReferralId SupportReferralId, ReferralStatus Status, DateTimeOffset OccurredAt) : IDomainEvent;
