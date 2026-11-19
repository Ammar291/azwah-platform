using FamilyResilience.Domain.Safeguarding;

namespace FamilyResilience.Application.Assistant;

/// <summary>
/// Keeps lines that would trip the voice provider's filter out of the earlier conversation a call starts from (D-141). A
/// line the provider's filter acts on closes the session within seconds of the start, on every call after it, before
/// the person says anything (reproduced on 2026-11-10). Each line, the person's and the assistant's, goes through the
/// phrase screen and then, in one batch, the safety check; a line either flags is held back. The check may hold the
/// start up for at most <see cref="AssistantLimits.ConsultantHistoryCheckWait"/>; lines it has not cleared by then go
/// through on the phrase screen alone, and <see cref="ContinuingConsultantSession"/> reopens a call stopped before the
/// person spoke without them. Held-back lines raise nothing: they were said before this call. Nothing is logged.
/// </summary>
public sealed class ConsultantHistoryScreen(CrisisSignalScreen screen, IAssistantSafetyCheck safety, AssistantLimits limits)
{
    public async Task<ConsultantHistory> ScreenAsync(IReadOnlyList<AssistantMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            return new ConsultantHistory([], Withheld: false);
        }

        var held = messages.Select(message => screen.Screen(message.Text) is not null).ToArray();
        var unscreened = Enumerable.Range(0, messages.Count).Where(index => !held[index]).ToList();
        if (safety.IsConfigured && unscreened.Count > 0)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(limits.ConsultantHistoryCheckWait);
            try
            {
                var verdicts = await safety.CheckManyAsync(unscreened.Select(index => messages[index].Text).ToList(), wait.Token).WaitAsync(wait.Token);
                if (verdicts.Count == unscreened.Count)
                {
                    for (var i = 0; i < verdicts.Count; i++)
                    {
                        held[unscreened[i]] |= verdicts[i].IsDanger();
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Not checked in time: the call starts on the phrase screen alone.
            }
        }

        var kept = messages.Where((_, index) => !held[index]).ToList();
        return new ConsultantHistory(kept, Withheld: kept.Count < messages.Count);
    }
}
