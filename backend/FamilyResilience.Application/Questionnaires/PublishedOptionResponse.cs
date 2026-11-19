namespace FamilyResilience.Application.Questionnaires;

/// <summary>An answer choice as participants see it. Its scored value stays on the server; clients send the ID.</summary>
public sealed record PublishedOptionResponse(Guid Id, string Code, int DisplayOrder, bool IsNonSubstantive, TextDto Label);
