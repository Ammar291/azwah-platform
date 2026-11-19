namespace FamilyResilience.Application.Assistant;

/// <summary>Whether the text assistant, the live consultant and the written score report each have a model configured.</summary>
public sealed record AssistantAvailabilityResponse(bool Assistant, bool Consultant, bool ScoreReport);
