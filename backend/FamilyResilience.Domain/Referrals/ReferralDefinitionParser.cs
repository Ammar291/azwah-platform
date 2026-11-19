using System.Text.Json;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Referrals;

/// <summary>
/// Reads a <c>referral.v1</c> rule-set definition strictly:
/// <c>{"schema":"referral.v1","target":{"sectionCode":"A"}|null,"routes":[{"categoryCode":"…","when":{"tierCodeIn":["…"]}}]}</c>.
/// Unknown or repeated keys, empty lists, repeated categories and codes that are not snake case are refused.
/// </summary>
public static class ReferralDefinitionParser
{
    public const string Schema = "referral.v1";
    public const int MaxRoutes = 50;

    private static readonly string[] RootKeys = ["schema", "target", "routes"];
    private static readonly string[] TargetKeys = ["sectionCode"];
    private static readonly string[] RouteKeys = ["categoryCode", "when"];
    private static readonly string[] WhenKeys = ["tierCodeIn"];

    public static Result<ReferralDefinition> Parse(string? definition)
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

            var target = ReadTarget(root);
            if (target.IsFailure)
            {
                return Result.Failure<ReferralDefinition>(target.Error!);
            }

            if (!root.TryGetProperty("routes", out var routes)
                || routes.ValueKind != JsonValueKind.Array
                || routes.GetArrayLength() is 0 or > MaxRoutes)
            {
                return Invalid("$.routes", $"must be a list of 1 to {MaxRoutes} routes");
            }

            var parsed = new List<ReferralRoute>();
            var index = 0;
            foreach (var route in routes.EnumerateArray())
            {
                var read = ReadRoute(route, $"$.routes[{index}]");
                if (read.IsFailure)
                {
                    return Result.Failure<ReferralDefinition>(read.Error!);
                }

                if (parsed.Exists(existing => existing.CategoryCode == read.Value.CategoryCode))
                {
                    return Invalid($"$.routes[{index}].categoryCode", "appears in another route");
                }

                parsed.Add(read.Value);
                index++;
            }

            return Result.Success(new ReferralDefinition(target.Value, parsed));
        }
    }

    private static Result<string?> ReadTarget(JsonElement root)
    {
        if (!root.TryGetProperty("target", out var target) || target.ValueKind == JsonValueKind.Null)
        {
            return Result.Success<string?>(null);
        }

        if (target.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<string?>(Error("$.target", "must be an object or null"));
        }

        if (JsonObjects.FindUnexpectedKey(target, TargetKeys) is { } unexpected)
        {
            return Result.Failure<string?>(Error($"$.target.{unexpected}", "is not a known key or appears twice"));
        }

        return target.TryGetProperty("sectionCode", out var code) && code.ValueKind == JsonValueKind.String
            && Codes.IsContentCode(code.GetString())
            ? Result.Success<string?>(code.GetString())
            : Result.Failure<string?>(Error("$.target.sectionCode", "must be a section code such as \"A\""));
    }

    private static Result<ReferralRoute> ReadRoute(JsonElement route, string path)
    {
        if (route.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<ReferralRoute>(Error(path, "must be an object"));
        }

        if (JsonObjects.FindUnexpectedKey(route, RouteKeys) is { } unexpected)
        {
            return Result.Failure<ReferralRoute>(Error($"{path}.{unexpected}", "is not a known key or appears twice"));
        }

        if (!route.TryGetProperty("categoryCode", out var category)
            || category.ValueKind != JsonValueKind.String
            || !Codes.IsSnakeCode(category.GetString()))
        {
            return Result.Failure<ReferralRoute>(Error($"{path}.categoryCode", "must be a snake-case category code"));
        }

        if (!route.TryGetProperty("when", out var when) || when.ValueKind != JsonValueKind.Object)
        {
            return Result.Failure<ReferralRoute>(Error($"{path}.when", "must be an object"));
        }

        if (JsonObjects.FindUnexpectedKey(when, WhenKeys) is { } unexpectedCondition)
        {
            return Result.Failure<ReferralRoute>(Error($"{path}.when.{unexpectedCondition}", "is not a known condition or appears twice"));
        }

        if (!when.TryGetProperty("tierCodeIn", out var tiers) || tiers.ValueKind != JsonValueKind.Array || tiers.GetArrayLength() == 0)
        {
            return Result.Failure<ReferralRoute>(Error($"{path}.when.tierCodeIn", "must be a non-empty list of tier codes"));
        }

        var codes = new List<string>();
        foreach (var tier in tiers.EnumerateArray())
        {
            if (tier.ValueKind != JsonValueKind.String || tier.GetString() is not { } code || !Codes.IsSnakeCode(code) || codes.Contains(code))
            {
                return Result.Failure<ReferralRoute>(Error($"{path}.when.tierCodeIn", "must hold distinct snake-case tier codes"));
            }

            codes.Add(code);
        }

        return Result.Success(new ReferralRoute(category.GetString()!, codes));
    }

    private static Result<ReferralDefinition> Invalid(string path, string problem) =>
        Result.Failure<ReferralDefinition>(Error(path, problem));

    private static Error Error(string path, string problem) =>
        ReferralErrors.DefinitionInvalid with { Message = $"The referral definition is invalid at {path}: {problem}." };
}
