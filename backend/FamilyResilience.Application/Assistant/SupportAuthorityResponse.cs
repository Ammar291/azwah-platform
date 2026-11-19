using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.Application.Assistant;

/// <summary>An organisation on the All services page, in both languages, with no record of who reviewed it.</summary>
public sealed record SupportAuthorityResponse(
    string Code,
    string Kind,
    TextDto Name,
    TextDto Purpose,
    IReadOnlyList<string> Emirates,
    string? Website,
    string? Phone,
    IReadOnlyList<SupportServiceResponse> Services,
    DateOnly CheckedOn);
