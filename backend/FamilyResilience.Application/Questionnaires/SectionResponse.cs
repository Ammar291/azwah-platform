namespace FamilyResilience.Application.Questionnaires;

public sealed record SectionResponse(Guid Id, string Code, int DisplayOrder, TextDto Title, IReadOnlyList<ItemResponse> Items);
