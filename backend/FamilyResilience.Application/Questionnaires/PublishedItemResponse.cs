using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Application.Questionnaires;

/// <summary>A question as participants see it: no scoring direction or category (scoring happens on the server only).</summary>
public sealed record PublishedItemResponse(
    Guid Id, string Code, int DisplayOrder, ItemType ItemType, bool IsRequired, TextDto Prompt, IReadOnlyList<PublishedOptionResponse> Options);
