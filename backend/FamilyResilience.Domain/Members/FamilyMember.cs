using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Households;

namespace FamilyResilience.Domain.Members;

/// <summary>
/// A person in a household. Part of the <see cref="Household"/> aggregate: only the household creates and changes
/// members, so the roster rules hold. No name, identity number or contact detail is stored.
/// </summary>
public sealed class FamilyMember : Entity<FamilyMemberId>
{
    public const int AdultAge = 18;

    private FamilyMember(FamilyMemberId id)
        : base(id)
    {
    }

    public HouseholdId HouseholdId { get; private set; }

    public YearMonth BirthMonth { get; private set; } = null!;

    public HouseholdRelationship? RelationshipToHousehold { get; private set; }

    public EmploymentStatus? EmploymentStatus { get; private set; }

    public bool IsHeadOfHousehold { get; private set; }

    public bool IsPrimaryIncomeEarner { get; private set; }

    public MemberStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsActive => Status == MemberStatus.Active;

    public int AgeOn(DateOnly date) => BirthMonth.AgeOn(date);

    /// <summary>
    /// Whether the member is 18 or over on <paramref name="date"/> whatever their day of birth. Every adults-only
    /// safeguard (consent, assignment, eligibility, own account; D-58) uses this, never <see cref="AgeOn"/>, which can
    /// call a 17-year-old 18 during the month of their birthday.
    /// </summary>
    public bool IsCertainlyAdultOn(DateOnly date) => BirthMonth.IsCertainlyAtLeast(AdultAge, date);

    internal static FamilyMember Create(HouseholdId householdId, FamilyMemberDetails details, DateTimeOffset now)
    {
        var member = new FamilyMember(FamilyMemberId.New())
        {
            HouseholdId = householdId,
            Status = MemberStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        member.Apply(details);
        return member;
    }

    internal void Update(FamilyMemberDetails details, DateTimeOffset now)
    {
        Apply(details);
        UpdatedAt = now;
    }

    internal void Deactivate(DateTimeOffset now)
    {
        Status = MemberStatus.Inactive;
        UpdatedAt = now;
    }

    private void Apply(FamilyMemberDetails details)
    {
        BirthMonth = details.BirthMonth;
        RelationshipToHousehold = details.RelationshipToHousehold;
        EmploymentStatus = details.EmploymentStatus;
        IsHeadOfHousehold = details.IsHeadOfHousehold;
        IsPrimaryIncomeEarner = details.IsPrimaryIncomeEarner;
    }
}
