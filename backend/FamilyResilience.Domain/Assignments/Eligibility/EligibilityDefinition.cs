namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>A parsed <c>eligibility.v1</c> definition: the module it targets (null for the whole version) and its rule.</summary>
public sealed record EligibilityDefinition(string? TargetSectionCode, IEligibilitySpecification Rule);
