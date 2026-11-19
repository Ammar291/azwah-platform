namespace FamilyResilience.Application.Sessions;

/// <summary>One answer: the item and the chosen option, both from the session's published version. No free text.</summary>
public sealed record AnswerRequest(Guid ItemId, Guid OptionId);
