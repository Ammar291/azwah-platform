using System.Text.Json.Serialization;
using FamilyResilience.Application.Abstractions;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Households;

/// <summary>
/// A JSON merge patch of one member (RFC 7396): absent fields stay as they are; <c>null</c> clears the relationship
/// or the employment status. <c>"status": "inactive"</c> removes the member from the roster and cannot be combined
/// with other changes. To move the head or primary-earner flag, clear it on the old member first.
/// </summary>
public sealed record UpdateMemberRequest
{
    public Optional<int> BirthYear { get; init; }

    public Optional<int> BirthMonth { get; init; }

    public Optional<HouseholdRelationship?> RelationshipToHousehold { get; init; }

    public Optional<EmploymentStatus?> EmploymentStatus { get; init; }

    public Optional<bool> IsHeadOfHousehold { get; init; }

    public Optional<bool> IsPrimaryIncomeEarner { get; init; }

    public Optional<MemberStatus> Status { get; init; }

    [JsonIgnore]
    public bool ChangesDetails =>
        BirthYear.IsSet || BirthMonth.IsSet || RelationshipToHousehold.IsSet || EmploymentStatus.IsSet
        || IsHeadOfHousehold.IsSet || IsPrimaryIncomeEarner.IsSet;
}
