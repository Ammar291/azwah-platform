namespace FamilyResilience.Application.Assistant;

/// <summary>The tokens a model call used, for the metrics only.</summary>
public sealed record AssistantModelUsage(long InputTokens, long OutputTokens)
{
    public static AssistantModelUsage None { get; } = new(0, 0);
}
