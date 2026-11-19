namespace FamilyResilience.Application.Questionnaires;

public sealed record ContentOptionDto(string Code, int DisplayOrder, int Value, bool IsNonSubstantive, TextDto Label);
