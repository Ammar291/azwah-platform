namespace FamilyResilience.Domain.Privacy;

/// <summary>What a family asks about its data.</summary>
public enum PrivacyRequestType
{
    /// <summary>A copy of the data, beyond the download the app offers.</summary>
    Access = 1,

    /// <summary>Something recorded about them is wrong.</summary>
    Correction = 2,

    /// <summary>Delete the household's data (carried out with the erasure procedure, D-88).</summary>
    Erasure = 3,
}
