namespace FamilyResilience.Application.Assistant;

/// <summary>
/// One live consultant session per token subject, in this API instance: claiming a slot for a subject ends the
/// subject's earlier slot (its <see cref="ConsultantSlot.Replaced"/> token is cancelled), so a second start closes the
/// first. Registered as a singleton. Like the rate limits, it has no shared store across instances.
/// </summary>
public sealed class LiveConsultantSessions
{
    private readonly Dictionary<string, ConsultantSlot> _slots = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>How many subjects hold a slot now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _slots.Count;
            }
        }
    }

    /// <summary>A new slot for <paramref name="subject"/>; the subject's earlier slot, if any, is replaced.</summary>
    public ConsultantSlot Claim(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var slot = new ConsultantSlot(this, subject);
        ConsultantSlot? previous;
        lock (_gate)
        {
            _slots.TryGetValue(subject, out previous);
            _slots[subject] = slot;
        }

        previous?.Replace();
        return slot;
    }

    internal void Release(ConsultantSlot slot)
    {
        lock (_gate)
        {
            if (_slots.TryGetValue(slot.Subject, out var current) && ReferenceEquals(current, slot))
            {
                _slots.Remove(slot.Subject);
            }
        }
    }
}
