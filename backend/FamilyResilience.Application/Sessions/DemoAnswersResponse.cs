namespace FamilyResilience.Application.Sessions;

/// <summary>
/// Answers a demonstration can save for a session, one per item in item order, in the shape the answers endpoint
/// takes (D-130). Nothing is saved yet, and no score or band is returned: the result comes from submitting.
/// </summary>
public sealed record DemoAnswersResponse(IReadOnlyList<AnswerRequest> Responses);
