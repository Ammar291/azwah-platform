namespace FamilyResilience.Domain.Safeguarding;

/// <summary>A versioned list of crisis phrase rules, in file order.</summary>
public sealed record CrisisSignalList(string Version, IReadOnlyList<CrisisSignalRule> Rules);
