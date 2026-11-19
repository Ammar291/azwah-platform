namespace FamilyResilience.Domain.Scoring;

/// <summary>Everything a strategy needs, with no database access: the content in scope, validated parameters and the answers.</summary>
public sealed record ScoringInput(ScoringContent Content, ScoringParameters Parameters, IReadOnlyList<ScoringResponse> Responses);
