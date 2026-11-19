using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>The guard's decision. <see cref="Selected"/> is empty unless the kind is Accepted.</summary>
public sealed record SelectionVerdict(SelectionVerdictKind Kind, IReadOnlyList<SupportResource> Selected);
