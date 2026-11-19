namespace FamilyResilience.Application.Households;

/// <summary>A household that withdrew, as the retention job needs it: its ID and the reference erasure confirms.</summary>
public sealed record WithdrawnHousehold(Guid Id, string Reference);
