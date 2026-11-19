namespace FamilyResilience.Application.Assistant;

/// <summary>An approved support-directory entry a reply names, in the request's language, with its own link (D-122).</summary>
public sealed record AssistantSourceResponse(Guid Id, string Name, string Provider, string Url);
