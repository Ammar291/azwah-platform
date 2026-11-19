using FamilyResilience.Domain.Households;
using FamilyResilience.Domain.Identity;

namespace FamilyResilience.Application.Identity;

/// <summary>
/// The signed-in caller's platform user and the household of their active membership, if any. The household is a
/// read-only snapshot for access decisions: never change it or save it.
/// </summary>
public sealed record CallerRecord(PlatformUserId UserId, Household? Household);
