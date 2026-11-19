namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Answers to record or replace, at most one per item. Items not listed keep their answers, so sending the same body
/// twice leaves the same state.
/// </summary>
public sealed record SaveResponsesRequest(IReadOnlyList<AnswerRequest>? Responses);
