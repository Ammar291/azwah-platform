namespace FamilyResilience.Application.SupportResources;

/// <summary>How many of a search's usable terms a resource must contain to be found.</summary>
public enum TermCoverage
{
    /// <summary>At least half of the usable terms (rounded up), and at least one.</summary>
    Half,

    /// <summary>At least one usable term; used to gather candidates.</summary>
    AnyTerm,
}
