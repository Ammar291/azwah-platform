using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>
/// What a household tells us about one member. No name or identity number is ever collected. The flags are nullable
/// only so that a missing flag answers 400 instead of silently meaning false.
/// </summary>
public interface IMemberDetailsRequest
{
    int BirthYear { get; }

    int BirthMonth { get; }

    HouseholdRelationship? RelationshipToHousehold { get; }

    EmploymentStatus? EmploymentStatus { get; }

    bool? IsHeadOfHousehold { get; }

    bool? IsPrimaryIncomeEarner { get; }
}
