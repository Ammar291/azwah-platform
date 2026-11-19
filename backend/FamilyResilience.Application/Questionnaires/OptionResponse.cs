namespace FamilyResilience.Application.Questionnaires;

public sealed record OptionResponse(Guid Id, string Code, int DisplayOrder, int Value, bool IsNonSubstantive, TextDto Label);
