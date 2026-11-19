using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

public sealed record ScoringOption(ResponseOptionId OptionId, int Value, bool IsNonSubstantive);
