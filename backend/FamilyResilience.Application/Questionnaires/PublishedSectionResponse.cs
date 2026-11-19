namespace FamilyResilience.Application.Questionnaires;

public sealed record PublishedSectionResponse(
    Guid Id, string Code, int DisplayOrder, TextDto Title, IReadOnlyList<PublishedItemResponse> Items);
