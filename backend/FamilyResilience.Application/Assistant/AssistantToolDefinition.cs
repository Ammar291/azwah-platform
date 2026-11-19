namespace FamilyResilience.Application.Assistant;

/// <summary>A tool as a model sees it: its name, what it is for, and its arguments as a JSON Schema object (D-122).</summary>
public sealed record AssistantToolDefinition(string Name, string Description, string ParametersSchema);
