namespace FamilyResilience.Domain.Assignments.Eligibility;

/// <summary>The result of one rule: true, false with the reason, or unknown with the facts that are missing.</summary>
public sealed class SpecificationResult
{
    private SpecificationResult(Truth truth, string? reasonCode, IReadOnlyList<string> missingInformation)
    {
        Truth = truth;
        ReasonCode = reasonCode;
        MissingInformation = missingInformation;
    }

    public static SpecificationResult Satisfied { get; } = new(Truth.True, null, []);

    public Truth Truth { get; }

    /// <summary>Set only when <see cref="Truth"/> is False.</summary>
    public string? ReasonCode { get; }

    /// <summary>Set only when <see cref="Truth"/> is Unknown.</summary>
    public IReadOnlyList<string> MissingInformation { get; }

    public static SpecificationResult NotSatisfied(string reasonCode) => new(Truth.False, reasonCode, []);

    public static SpecificationResult Unknown(IReadOnlyList<string> missingInformation) =>
        new(Truth.Unknown, null, missingInformation);

    /// <summary>Unknown, naming every fact missing from <paramref name="unknown"/> once, in a stable order.</summary>
    public static SpecificationResult UnknownFrom(IEnumerable<SpecificationResult> unknown) =>
        Unknown(unknown
            .SelectMany(result => result.MissingInformation)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList());
}
