using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FamilyResilience.Domain.Common;

namespace FamilyResilience.Domain.Households;

/// <summary>
/// A non-sensitive household reference such as <c>HH-7K3M-Q9TX</c>, safe to read out on the phone. It is random and
/// never derived from the household ID or any personal detail. Crockford base 32 avoids I, L, O and U.
/// </summary>
public sealed partial record HouseholdReference
{
    public const int Length = 12;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private HouseholdReference(string value) => Value = value;

    public string Value { get; }

    public static HouseholdReference Generate()
    {
        var random = RandomNumberGenerator.GetString(Alphabet, 8);
        return new HouseholdReference($"HH-{random[..4]}-{random[4..]}");
    }

    public static Result<HouseholdReference> Parse(string? value) =>
        value is not null && Format().IsMatch(value)
            ? Result.Success(new HouseholdReference(value))
            : Result.Failure<HouseholdReference>(HouseholdErrors.ReferenceInvalid);

    public override string ToString() => Value;

    [GeneratedRegex(@"^HH-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Format();
}
