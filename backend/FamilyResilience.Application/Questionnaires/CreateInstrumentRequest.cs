using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed record CreateInstrumentRequest(string Code, InstrumentKind Kind, TextDto Name, TextDto Description);
