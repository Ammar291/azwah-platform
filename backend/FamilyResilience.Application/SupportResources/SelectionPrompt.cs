using FamilyResilience.Domain.SupportResources;

namespace FamilyResilience.Application.SupportResources;

/// <summary>A model request plus the map from the handles it mentions back to the directory entries.</summary>
public sealed record SelectionPrompt(LanguageModelRequest Request, IReadOnlyDictionary<string, SupportResource> Handles);
