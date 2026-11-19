namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The earlier conversation a call may start from (<see cref="ConsultantHistoryScreen"/>, D-141), and whether any line
/// of it was held back. Never logged: <see cref="ToString"/> prints counts only.
/// </summary>
public sealed record ConsultantHistory(IReadOnlyList<AssistantMessage> Kept, bool Withheld)
{
    public override string ToString() => $"ConsultantHistory {{ Kept = {Kept.Count}, Withheld = {Withheld} }}";
}
