using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>One recorded answer as the scoring strategy sees it.</summary>
public sealed record ScoringResponse(InstrumentItemId ItemId, ResponseOptionId OptionId, int RawValue);
