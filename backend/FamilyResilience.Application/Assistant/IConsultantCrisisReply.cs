namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Writes what the consultant says when the provider's filter stopped a call over what the person said (D-141): a short,
/// calm, supportive reply in the call's language, under <see cref="AssistantPrompt.CrisisReplyInstructions"/>. The
/// person's words go to the model for this and are never logged or kept. Null when nothing could be written; the
/// consultant then says <see cref="AssistantPrompt.CrisisFallbackWords"/>. The default,
/// <see cref="NoConsultantCrisisReply"/>, writes nothing.
/// </summary>
public interface IConsultantCrisisReply
{
    bool IsConfigured { get; }

    /// <summary>The words to say, or null. <paramref name="said"/> is the person's latest line on the call.</summary>
    Task<string?> WriteAsync(string language, string said, CancellationToken cancellationToken);
}
