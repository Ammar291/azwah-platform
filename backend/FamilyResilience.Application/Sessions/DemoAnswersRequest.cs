namespace FamilyResilience.Application.Sessions;

/// <summary>The result a demonstration asks for: <c>low</c>, <c>medium</c> or <c>high</c> (D-130).</summary>
public sealed record DemoAnswersRequest(string? Level);
