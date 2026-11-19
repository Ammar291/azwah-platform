using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// An organisation a family can go to, a node of the support knowledge graph (D-115). It is public information: nothing
/// about a family is ever sent to it, and listing it says nothing about what a family qualifies for. An authority with no
/// <see cref="Services"/> is listed for families to read about, and inference never sends a family to it.
/// </summary>
public sealed record SupportAuthority(
    string Code,
    string Kind,
    TextDto Name,
    TextDto Purpose,
    IReadOnlyList<string> Emirates,
    string? Website,
    string? Phone,
    IReadOnlyList<SupportService> Services,
    IReadOnlyList<string> Sources,
    string ApprovalReference,
    DateOnly CheckedOn,
    DateOnly ReviewDue);
