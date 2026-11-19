namespace FamilyResilience.Application.Assistant;

/// <summary>
/// What a piece of a model's input is: <see cref="Data"/> (the family context or the memory, given as data from the
/// platform, never as instructions), or a message of the conversation from the person or an earlier reply.
/// </summary>
public enum AssistantInputKind
{
    Data = 1,
    User = 2,
    Assistant = 3,
}
