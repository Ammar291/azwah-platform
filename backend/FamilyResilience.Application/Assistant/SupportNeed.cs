using FamilyResilience.Application.Questionnaires;

namespace FamilyResilience.Application.Assistant;

/// <summary>A kind of help a family may need, the node joining questions to services in the knowledge graph (D-115).</summary>
public sealed record SupportNeed(string Code, TextDto Label);
