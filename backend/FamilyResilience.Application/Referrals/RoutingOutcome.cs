namespace FamilyResilience.Application.Referrals;

/// <summary>How many routing decisions a score produced, and how many of them became consented referrals.</summary>
public sealed record RoutingOutcome(int Recommendations, int Referrals)
{
    public static RoutingOutcome None { get; } = new(0, 0);
}
