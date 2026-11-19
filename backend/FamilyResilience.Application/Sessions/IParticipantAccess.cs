using FamilyResilience.Domain.Members;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Whether the signed-in caller may act for a household member. Production resolves
/// <c>Identity.CallerAccess</c>, which decides from the caller's household membership (D-49).
/// </summary>
public interface IParticipantAccess
{
    Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken);

    /// <summary>Whether the caller signs in as their household's active head; only the head sees scores (D-103).</summary>
    Task<bool> IsHeadOfHouseholdAsync(CancellationToken cancellationToken);
}
