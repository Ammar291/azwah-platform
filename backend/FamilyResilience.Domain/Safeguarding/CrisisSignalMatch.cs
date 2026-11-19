namespace FamilyResilience.Domain.Safeguarding;

/// <summary>The rule that matched. It never carries the matched text.</summary>
public sealed record CrisisSignalMatch(string RuleCode, SafetyConcern Concern);
