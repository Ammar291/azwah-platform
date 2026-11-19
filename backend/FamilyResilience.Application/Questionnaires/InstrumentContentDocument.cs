namespace FamilyResilience.Application.Questionnaires;

/// <summary>The full content of one draft version: the import request body and the shape of committed content files.</summary>
public sealed record InstrumentContentDocument(
    string ContentReferenceAr, string ContentReferenceEn, IReadOnlyList<ContentSectionDto> Sections);
