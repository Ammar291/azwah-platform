namespace FamilyResilience.Application.Questionnaires;

public sealed record ContentSectionDto(string Code, int DisplayOrder, TextDto Title, IReadOnlyList<ContentItemDto> Items);
