namespace FamilyResilience.Application.Households;

/// <summary>
/// What erasure does with a household's consent records (<c>Erasure:ConsentRecords</c>). There is no default: until the
/// approvers answer open question 21, erasure refuses to run. Only <c>delete</c> is built; keeping consent evidence
/// after an erasure needs its own design once the answer is known.
/// </summary>
public sealed record ErasureSettings(bool DeletesConsentRecords)
{
    public const string Setting = "Erasure:ConsentRecords";

    public static ErasureSettings From(string? configured) =>
        new(string.Equals(configured?.Trim(), "delete", StringComparison.Ordinal));
}
