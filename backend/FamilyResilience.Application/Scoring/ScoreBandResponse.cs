namespace FamilyResilience.Application.Scoring;

/// <summary>One band of a grading scale, both bounds included, as the approved rule set states it.</summary>
public sealed record ScoreBandResponse(string Code, int Min, int Max);
