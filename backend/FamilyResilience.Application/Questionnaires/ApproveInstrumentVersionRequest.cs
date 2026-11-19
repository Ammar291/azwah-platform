namespace FamilyResilience.Application.Questionnaires;

public sealed record ApproveInstrumentVersionRequest(string ApprovalSource, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);
