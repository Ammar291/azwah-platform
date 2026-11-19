namespace FamilyResilience.Application.SupportResources;

/// <summary>The raw JSON text a model returned and the tokens it used. Never trusted until the guard accepts it.</summary>
public sealed record LanguageModelReply(string Json, int InputTokens, int OutputTokens)
{
    /// <summary>Prints the reply's length, never its text, which a model could have filled with the search text.</summary>
    public override string ToString() =>
        $"LanguageModelReply {{ JsonLength = {Json.Length}, InputTokens = {InputTokens}, OutputTokens = {OutputTokens} }}";
}
