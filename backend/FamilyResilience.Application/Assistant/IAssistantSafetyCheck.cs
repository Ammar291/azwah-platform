namespace FamilyResilience.Application.Assistant;

/// <summary>
/// A check of what the person said for signs that someone may be in danger (D-126), run alongside the model so it adds
/// no wait. It never throws: a failure is <see cref="AssistantSafetyVerdict.NotChecked"/>. The text is never logged.
/// </summary>
public interface IAssistantSafetyCheck
{
    bool IsConfigured { get; }

    Task<AssistantSafetyVerdict> CheckAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// One verdict per text, in order (D-141: a call's earlier conversation is checked before it is replayed to the voice
    /// model). By default each text is checked on its own, all at once; an adapter may send them together.
    /// </summary>
    async Task<IReadOnlyList<AssistantSafetyVerdict>> CheckManyAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return await Task.WhenAll(texts.Select(text => CheckAsync(text, cancellationToken)));
    }
}
