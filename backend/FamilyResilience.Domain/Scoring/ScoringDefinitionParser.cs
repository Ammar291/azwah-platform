using System.Text.Json;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Scoring;

/// <summary>Reads a <c>scoring.v1</c> rule-set definition strictly: target, policy, algorithm version and parameters.</summary>
public static class ScoringDefinitionParser
{
    public const string Schema = "scoring.v1";

    private static readonly string[] RootKeys = ["schema", "target", "policy", "algorithmVersion", "parameters"];
    private static readonly string[] TargetKeys = ["sectionCode"];

    public static Result<ScoringDefinition> Parse(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return Invalid("$", "the definition is empty");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(definition);
        }
        catch (JsonException)
        {
            return Invalid("$", "the definition is not valid JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("$", "must be an object");
            }

            if (JsonObjects.FindUnexpectedKey(root, RootKeys) is { } unexpected)
            {
                return Invalid($"$.{unexpected}", "is not a known key or appears twice");
            }

            if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind != JsonValueKind.String || schema.GetString() != Schema)
            {
                return Invalid("$.schema", $"must be \"{Schema}\"");
            }

            string? sectionCode = null;
            if (root.TryGetProperty("target", out var target) && target.ValueKind != JsonValueKind.Null)
            {
                if (target.ValueKind != JsonValueKind.Object)
                {
                    return Invalid("$.target", "must be an object or null");
                }

                if (JsonObjects.FindUnexpectedKey(target, TargetKeys) is { } unexpectedTarget)
                {
                    return Invalid($"$.target.{unexpectedTarget}", "is not a known key or appears twice");
                }

                if (!target.TryGetProperty("sectionCode", out var code)
                    || code.ValueKind != JsonValueKind.String
                    || !Codes.IsContentCode(code.GetString()))
                {
                    return Invalid("$.target.sectionCode", "must be a section code such as \"A\"");
                }

                sectionCode = code.GetString();
            }

            if (!root.TryGetProperty("policy", out var policy)
                || policy.ValueKind != JsonValueKind.String
                || policy.GetString() is not { } policyCode
                || !Codes.IsSnakeCode(policyCode))
            {
                return Invalid("$.policy", "must be a snake-case policy code");
            }

            if (!root.TryGetProperty("algorithmVersion", out var version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var algorithmVersion)
                || algorithmVersion < 1)
            {
                return Invalid("$.algorithmVersion", "must be a whole number of 1 or more");
            }

            if (!root.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            {
                return Invalid("$.parameters", "must be an object");
            }

            return Result.Success(new ScoringDefinition(sectionCode, policyCode, algorithmVersion, parameters.Clone()));
        }
    }

    private static Result<ScoringDefinition> Invalid(string path, string problem) =>
        Result.Failure<ScoringDefinition>(ScoringErrors.DefinitionInvalid with
        {
            Message = $"The scoring definition is invalid at {path}: {problem}.",
        });
}
