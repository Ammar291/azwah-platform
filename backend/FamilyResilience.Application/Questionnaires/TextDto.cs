namespace FamilyResilience.Application.Questionnaires;

/// <summary>Bilingual text in requests and content files. Arabic may be null in drafts.</summary>
public sealed record TextDto(string? Ar, string En);
