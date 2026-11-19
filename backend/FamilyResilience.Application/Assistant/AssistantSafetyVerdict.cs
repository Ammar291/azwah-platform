namespace FamilyResilience.Application.Assistant;

/// <summary>What the safety check found in what the person said (D-126, D-136).</summary>
public enum AssistantSafetyVerdict
{
    /// <summary>No check ran (none configured, it failed, or it ran out of time): nothing changes.</summary>
    NotChecked = 0,

    Clear = 1,

    /// <summary>Self-harm: the reply offers the SOS screen whatever the model wrote. Wins over danger of violence.</summary>
    DangerToSelf = 2,

    /// <summary>
    /// Violence or a threat: the reply offers the SOS screen whatever the model wrote. The check cannot tell who is at
    /// risk (the person describing being hurt, or someone they threaten), so it is not taken as harm to others.
    /// </summary>
    DangerOfViolence = 3,
}
