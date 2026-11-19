using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Assistant;

/// <summary>Questions about an <see cref="AssistantSafetyVerdict"/>, asked the same way by the chat and the consultant.</summary>
public static class AssistantSafetyVerdicts
{
    /// <summary>True for danger to self or of violence: the reply offers SOS and the signal is recorded for staff.</summary>
    public static bool IsDanger(this AssistantSafetyVerdict verdict) =>
        verdict is AssistantSafetyVerdict.DangerToSelf or AssistantSafetyVerdict.DangerOfViolence;

    /// <summary>
    /// The concern a danger verdict is recorded as (D-136): danger to self is suicide or self-harm; danger of violence is
    /// abuse or danger, because moderation's violence and threat categories cannot tell who is at risk, and a victim's
    /// "my husband hits me" must not be filed as harm to others. Only a danger verdict has one.
    /// </summary>
    public static SafetyConcern Concern(this AssistantSafetyVerdict verdict) => verdict switch
    {
        AssistantSafetyVerdict.DangerToSelf => SafetyConcern.SuicideOrSelfHarm,
        AssistantSafetyVerdict.DangerOfViolence => SafetyConcern.AbuseOrDanger,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Only a danger verdict has a concern."),
    };
}
