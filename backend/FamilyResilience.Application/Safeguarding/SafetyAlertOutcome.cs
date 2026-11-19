namespace FamilyResilience.Application.Safeguarding;

/// <summary>What recording a signal did. <see cref="Failed"/> is logged and counted; the caller's request goes on.</summary>
public enum SafetyAlertOutcome
{
    /// <summary>A new alert was raised.</summary>
    Raised = 1,

    /// <summary>The signal folded into the subject's unresolved alert about the same concern from the same source.</summary>
    Repeated,

    /// <summary>Nothing to record: the server has no record of the caller, or the session already has its alert.</summary>
    Skipped,

    /// <summary>The alert could not be saved.</summary>
    Failed,
}
