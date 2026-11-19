using System.Text.Json;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Members;

namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>
/// Reads an <c>eligibility.v1</c> rule-set definition strictly. Every rule node is an object with exactly one known
/// key; anything else is refused with the JSON path of the problem, so an approved definition always means one thing.
/// </summary>
public static class EligibilityDefinitionParser
{
    public const string Schema = "eligibility.v1";
    public const int MaxDepth = 8;
    public const int MaxAge = 130;

    private static readonly string[] RootKeys = ["schema", "target", "rule"];
    private static readonly string[] TargetKeys = ["sectionCode"];

    private static readonly Dictionary<string, EmploymentStatus> EmploymentStatuses = Enum.GetValues<EmploymentStatus>()
        .ToDictionary(status => JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString()), StringComparer.Ordinal);

    public static Result<EligibilityDefinition> Parse(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return Invalid<EligibilityDefinition>("$", "the definition is empty");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(definition);
        }
        catch (JsonException)
        {
            return Invalid<EligibilityDefinition>("$", "the definition is not valid JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid<EligibilityDefinition>("$", "must be an object");
            }

            if (JsonObjects.FindUnexpectedKey(root, RootKeys) is { } unexpected)
            {
                return Invalid<EligibilityDefinition>($"$.{unexpected}", "is not a known key or appears twice");
            }

            if (!root.TryGetProperty("schema", out var schema)
                || schema.ValueKind != JsonValueKind.String
                || schema.GetString() != Schema)
            {
                return Invalid<EligibilityDefinition>("$.schema", $"must be \"{Schema}\"");
            }

            var target = ParseTarget(root);
            if (target.IsFailure)
            {
                return Result.Failure<EligibilityDefinition>(target.Error!);
            }

            if (!root.TryGetProperty("rule", out var rule))
            {
                return Invalid<EligibilityDefinition>("$.rule", "is required");
            }

            var parsed = ParseNode(rule, "$.rule", 1);
            return parsed.IsFailure
                ? Result.Failure<EligibilityDefinition>(parsed.Error!)
                : Result.Success(new EligibilityDefinition(target.Value, parsed.Value));
        }
    }

    private static Result<string?> ParseTarget(JsonElement root)
    {
        if (!root.TryGetProperty("target", out var target) || target.ValueKind == JsonValueKind.Null)
        {
            return Result.Success<string?>(null);
        }

        if (target.ValueKind != JsonValueKind.Object)
        {
            return Invalid<string?>("$.target", "must be an object or null");
        }

        if (JsonObjects.FindUnexpectedKey(target, TargetKeys) is { } unexpected)
        {
            return Invalid<string?>($"$.target.{unexpected}", "is not a known key or appears twice");
        }

        return target.TryGetProperty("sectionCode", out var code)
               && code.ValueKind == JsonValueKind.String
               && Codes.IsContentCode(code.GetString())
            ? Result.Success<string?>(code.GetString())
            : Invalid<string?>("$.target.sectionCode", "must be a section code such as \"A\"");
    }

    private static Result<IEligibilitySpecification> ParseNode(JsonElement node, string path, int depth)
    {
        if (depth > MaxDepth)
        {
            return Invalid<IEligibilitySpecification>(path, $"rules nest deeper than {MaxDepth} levels");
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return Invalid<IEligibilitySpecification>(path, "must be an object holding one rule");
        }

        var properties = node.EnumerateObject().ToList();
        if (properties.Count != 1)
        {
            return Invalid<IEligibilitySpecification>(path, "must hold exactly one rule");
        }

        var (name, value) = (properties[0].Name, properties[0].Value);
        var at = $"{path}.{name}";
        return name switch
        {
            "all" => ParseList(value, at, depth, rules => new AllOf(rules)),
            "any" => ParseList(value, at, depth, rules => new AnyOf(rules)),
            "ageAtLeast" => ParseInteger(value, at, 0, MaxAge, age => new AgeAtLeast(age)),
            "ageAtMost" => ParseInteger(value, at, 0, MaxAge, age => new AgeAtMost(age)),
            "householdHasMemberYoungerThan" => ParseInteger(value, at, 1, MaxAge, age => new HouseholdHasMemberYoungerThan(age)),
            "employmentStatusIn" => ParseEmployment(value, at),
            "isHeadOfHousehold" => ParseTrue(value, at, new IsHeadOfHousehold()),
            "isPrimaryIncomeEarner" => ParseTrue(value, at, new IsPrimaryIncomeEarner()),
            _ => Invalid<IEligibilitySpecification>(at, "is not a known rule"),
        };
    }

    private static Result<IEligibilitySpecification> ParseList(
        JsonElement value,
        string path,
        int depth,
        Func<IReadOnlyList<IEligibilitySpecification>, IEligibilitySpecification> combine)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return Invalid<IEligibilitySpecification>(path, "must be a non-empty list of rules");
        }

        var rules = new List<IEligibilitySpecification>();
        var index = 0;
        foreach (var child in value.EnumerateArray())
        {
            var parsed = ParseNode(child, $"{path}[{index}]", depth + 1);
            if (parsed.IsFailure)
            {
                return parsed;
            }

            rules.Add(parsed.Value);
            index++;
        }

        return Result.Success(combine(rules));
    }

    private static Result<IEligibilitySpecification> ParseInteger(
        JsonElement value, string path, int minimum, int maximum, Func<int, IEligibilitySpecification> create) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= minimum && number <= maximum
            ? Result.Success(create(number))
            : Invalid<IEligibilitySpecification>(path, $"must be a whole number from {minimum} to {maximum}");

    private static Result<IEligibilitySpecification> ParseEmployment(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return Invalid<IEligibilitySpecification>(path, "must be a non-empty list of employment statuses");
        }

        var statuses = new HashSet<EmploymentStatus>();
        var index = 0;
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String
                || !EmploymentStatuses.TryGetValue(entry.GetString() ?? string.Empty, out var status)
                || !statuses.Add(status))
            {
                return Invalid<IEligibilitySpecification>(
                    $"{path}[{index}]", $"must be a distinct value from: {string.Join(", ", EmploymentStatuses.Keys)}");
            }

            index++;
        }

        return Result.Success<IEligibilitySpecification>(new EmploymentStatusIn(statuses));
    }

    private static Result<IEligibilitySpecification> ParseTrue(JsonElement value, string path, IEligibilitySpecification rule) =>
        value.ValueKind == JsonValueKind.True ? Result.Success(rule) : Invalid<IEligibilitySpecification>(path, "must be true");

    private static Result<T> Invalid<T>(string path, string problem) =>
        Result.Failure<T>(EligibilityErrors.DefinitionInvalid with
        {
            Message = $"The eligibility definition is invalid at {path}: {problem}.",
        });
}
