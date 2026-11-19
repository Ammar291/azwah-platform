namespace FamilyResilience.IntegrationTests.SupportResources.Evaluation;

/// <summary>
/// The standard ranked-retrieval measures the evaluation reports, over resource codes in the order a search returned
/// them.
/// </summary>
internal static class RetrievalMetrics
{
    /// <summary>Relevant results among the first <paramref name="k"/>, divided by <paramref name="k"/>.</summary>
    public static double PrecisionAtK(IReadOnlyList<string> returned, IReadOnlySet<string> relevant, int k)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        return (double)RelevantInTop(returned, relevant, k) / k;
    }

    /// <summary>Relevant results among the first <paramref name="k"/>, divided by the number of relevant results.</summary>
    public static double RecallAtK(IReadOnlyList<string> returned, IReadOnlySet<string> relevant, int k)
    {
        ArgumentNullException.ThrowIfNull(relevant);
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);
        if (relevant.Count == 0)
        {
            throw new ArgumentException("Recall needs at least one relevant result.", nameof(relevant));
        }

        return (double)RelevantInTop(returned, relevant, k) / relevant.Count;
    }

    /// <summary>One divided by the rank of the first relevant result, or 0 when none came back.</summary>
    public static double ReciprocalRank(IReadOnlyList<string> returned, IReadOnlySet<string> relevant)
    {
        ArgumentNullException.ThrowIfNull(returned);
        ArgumentNullException.ThrowIfNull(relevant);
        for (var index = 0; index < returned.Count; index++)
        {
            if (relevant.Contains(returned[index]))
            {
                return 1.0 / (index + 1);
            }
        }

        return 0;
    }

    /// <summary>
    /// The nearest-rank percentile: the smallest value with at least <paramref name="p"/> percent of the values at or
    /// below it (<paramref name="p"/> from 0 to 100).
    /// </summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfLessThan(p, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(p, 100);
        if (values.Count == 0)
        {
            throw new ArgumentException("A percentile needs at least one value.", nameof(values));
        }

        var sorted = values.Order().ToList();
        var rank = (int)Math.Ceiling(p * sorted.Count / 100);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    private static int RelevantInTop(IReadOnlyList<string> returned, IReadOnlySet<string> relevant, int k)
    {
        ArgumentNullException.ThrowIfNull(returned);
        ArgumentNullException.ThrowIfNull(relevant);
        return returned.Take(k).Count(relevant.Contains);
    }
}
