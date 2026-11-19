using System.Diagnostics;
using FamilyResilience.Application.Assistant;

namespace FamilyResilience.Evaluation;

/// <summary>The real supportive-reply writer with a stopwatch on it (D-141): how long each reply took and whether one came.</summary>
internal sealed class TimingCrisisReply(IConsultantCrisisReply inner) : IConsultantCrisisReply
{
    private readonly List<(TimeSpan Took, bool Written)> _calls = [];

    public bool IsConfigured => inner.IsConfigured;

    public IReadOnlyList<(TimeSpan Took, bool Written)> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public async Task<string?> WriteAsync(string language, string said, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        string? words = null;
        try
        {
            words = await inner.WriteAsync(language, said, cancellationToken);
            return words;
        }
        finally
        {
            lock (_calls)
            {
                _calls.Add((clock.Elapsed, words is not null));
            }
        }
    }
}
