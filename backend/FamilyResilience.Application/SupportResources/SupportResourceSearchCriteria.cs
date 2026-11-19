using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// One directory search. <see cref="Terms"/> come from <see cref="SupportResourceQueryText.Parse"/>; empty
/// <see cref="Categories"/> or <see cref="Audiences"/> mean no filter; <see cref="Visibilities"/> are decided on the
/// server from the caller's roles. Without usable terms the search browses the given categories, or finds nothing.
/// </summary>
public sealed record SupportResourceSearchCriteria(
    IReadOnlyList<SearchTerm> Terms,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Audiences,
    IReadOnlyCollection<SupportResourceVisibility> Visibilities,
    DateTimeOffset Now,
    TermCoverage Coverage,
    int Limit);
