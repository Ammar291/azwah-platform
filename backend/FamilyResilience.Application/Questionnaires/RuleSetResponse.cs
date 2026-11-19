using System.Text.Json;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

public sealed record RuleSetResponse(
    Guid Id,
    RuleSetKind Kind,
    string Code,
    int VersionNumber,
    Guid InstrumentVersionId,
    RuleSetStatus Status,
    JsonElement Definition,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAt,
    string? ApprovalSource,
    DateTimeOffset? RetiredAt);
