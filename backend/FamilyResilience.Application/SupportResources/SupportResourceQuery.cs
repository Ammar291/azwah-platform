using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// One directory search as the assistant receives it. <see cref="Text"/> is the person's original words, held only for
/// a model path; it is never stored, logged or counted. <see cref="Visibilities"/> come from the caller's token roles.
/// </summary>
public sealed record SupportResourceQuery(
    string? Text,
    IReadOnlyList<SearchTerm> Terms,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Audiences,
    IReadOnlyCollection<SupportResourceVisibility> Visibilities,
    int Limit)
{
    /// <summary>Never prints the search text or the terms made from it.</summary>
    public override string ToString() =>
        $"SupportResourceQuery {{ HasText = {!string.IsNullOrWhiteSpace(Text)}, Terms = {Terms.Count}, Categories = {Categories.Count}, Audiences = {Audiences.Count}, Limit = {Limit} }}";
}
