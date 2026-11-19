namespace FamilyResilience.Domain.Referrals;

/// <summary>
/// How a referral reached its partner. Only <see cref="Manual"/> exists: a support coordinator handed it over outside the
/// platform. No electronic hand-off to any partner is built (open questions 13 and 33).
/// </summary>
public enum HandOffMethod
{
    Manual = 1,
}
