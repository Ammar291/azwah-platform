namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One item of a model step's working state, handed back on the next step: what the model produced
/// (<see cref="AssistantModelOutputItem"/>, opaque to the Application) or a tool's output
/// (<see cref="AssistantToolOutputItem"/>).
/// </summary>
public abstract record AssistantContinuationItem;
