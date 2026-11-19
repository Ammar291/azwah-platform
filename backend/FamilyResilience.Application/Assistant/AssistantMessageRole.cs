namespace FamilyResilience.Application.Assistant;

/// <summary>Who wrote a chat message: the person (<c>user</c>) or an earlier reply (<c>assistant</c>).</summary>
public enum AssistantMessageRole
{
    User = 1,
    Assistant = 2,
}
