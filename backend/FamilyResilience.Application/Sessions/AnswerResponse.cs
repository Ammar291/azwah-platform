namespace FamilyResilience.Application.Sessions;

public sealed record AnswerResponse(Guid ItemId, Guid OptionId, DateTimeOffset AnsweredAt, DateTimeOffset LastModifiedAt);
