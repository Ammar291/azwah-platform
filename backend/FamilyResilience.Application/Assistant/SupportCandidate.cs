namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A service the graph reached from a family's answers. <see cref="Weight"/> is how much room to improve the answers leading
/// to its strongest need show, and only orders candidates; it is not a score and is never shown or stored.
/// </summary>
public sealed record SupportCandidate(SupportAuthority Authority, SupportService Service, SupportNeed Need, decimal Weight)
{
    /// <summary>Prints no weight: it comes from a family's answers.</summary>
    public override string ToString() => $"SupportCandidate {{ Service = {Service.Code} }}";
}
