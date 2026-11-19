using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed record InstrumentResponse(
    Guid Id, string Code, InstrumentKind Kind, TextDto Name, TextDto Description, InstrumentStatus Status, bool HasCompleteText);
