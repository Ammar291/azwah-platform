namespace FamilyResilience.Application.SupportResources;

/// <summary>What a person typed, plus optional filters. Sent in the body so the text never sits in a URL.</summary>
public sealed record SearchSupportResourcesRequest(
    string? Text, IReadOnlyList<string>? Categories, IReadOnlyList<string>? Audiences, int? Limit)
{
    /// <summary>Never prints the search text, so it cannot reach a log through string formatting.</summary>
    public override string ToString() =>
        $"SearchSupportResourcesRequest {{ HasText = {!string.IsNullOrWhiteSpace(Text)}, Categories = {Categories?.Count ?? 0}, Audiences = {Audiences?.Count ?? 0}, Limit = {Limit} }}";
}
