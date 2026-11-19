using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Domain.Referrals;

[DomainEventType("referrals.support_referral_created.v1")]
public sealed record SupportReferralCreated(
    SupportReferralId SupportReferralId, AssessmentSessionId AssessmentSessionId, DateTimeOffset OccurredAt) : IDomainEvent;
