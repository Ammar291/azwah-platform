using FamilyResilience.Application.Sessions;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.IntegrationTests.Support;

/// <summary>Test-only stand-in for the identity phase's access rule: every caller may act for every member and is the head.</summary>
public sealed class AllowAllParticipantAccess : IParticipantAccess
{
    public Task<bool> CanActForMemberAsync(FamilyMemberId memberId, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> IsHeadOfHouseholdAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
