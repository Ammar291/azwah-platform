using FamilyResilience.Domain.Sessions;

namespace FamilyResilience.Application.Sessions;

/// <summary>
/// The outcome of a submission: <c>submitted</c> the first time, <c>already_submitted</c> afterwards. <c>status</c> is
/// <c>scored</c> when an approved scoring rule set scored it in the same transaction, and <c>submitted</c> while none is
/// approved. Read the result from <c>GET /api/v1/sessions/{sessionId}/result</c>.
/// </summary>
public sealed record SubmissionResponse(Guid SessionId, SubmissionOutcome Outcome, SessionStatus Status, DateTimeOffset SubmittedAt);
