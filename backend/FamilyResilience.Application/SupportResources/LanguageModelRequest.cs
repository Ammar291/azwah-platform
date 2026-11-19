namespace FamilyResilience.Application.SupportResources;

/// <summary>Fixed instructions, one untrusted data document, and the JSON schema the reply must match.</summary>
public sealed record LanguageModelRequest(string Instructions, string Data, string JsonSchema, int MaxOutputTokens)
{
    /// <summary>Prints lengths only: the data holds the person's (redacted) search text, which never reaches a log.</summary>
    public override string ToString() =>
        $"LanguageModelRequest {{ InstructionsLength = {Instructions.Length}, DataLength = {Data.Length}, JsonSchemaLength = {JsonSchema.Length}, MaxOutputTokens = {MaxOutputTokens} }}";
}
