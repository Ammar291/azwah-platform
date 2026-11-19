using System.Text.RegularExpressions;

namespace FamilyResilience.Domain.Common;

/// <summary>Formats of the stable codes used across the platform.</summary>
public static partial class Codes
{
    public const int MaxSnakeCodeLength = 64;
    public const int MaxContentCodeLength = 32;

    /// <summary>Lowercase snake case, for example <c>household_assessment</c> or <c>tier_1</c>.</summary>
    public static bool IsSnakeCode(string? value) => value is not null && SnakeCode().IsMatch(value);

    /// <summary>Codes printed in questionnaire sources, for example <c>A1</c> or <c>B.2</c>.</summary>
    public static bool IsContentCode(string? value) => value is not null && ContentCode().IsMatch(value);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SnakeCode();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ContentCode();
}
