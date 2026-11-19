namespace FamilyResilience.Application.Safeguarding;

/// <summary>The staff list's status filter. Wire codes: open, acknowledged, resolved, unresolved (open or acknowledged).</summary>
public enum SafetyAlertStatusFilter
{
    Open = 1,
    Acknowledged,
    Resolved,
    Unresolved,
}
