using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>A session's answers in item display order. Sensitive personal information: never logged, reads are audited.</summary>
public sealed record SessionAnswersResponse(Guid SessionId, SessionStatus Status, IReadOnlyList<AnswerResponse> Responses);
