namespace FamilyResilience.Application.Analytics;

/// <summary>
/// Small-cell suppression for one group of counts that together make up a total published elsewhere (the age bands of
/// one breakdown). A cell is hidden when it covers fewer than <c>minimum</c> people (D-58): the count shown may be of
/// assignments or score results, and one member can hold several, so the decision is taken on the distinct members
/// behind each cell, never on the count itself. When exactly one cell is hidden, the non-zero cell covering the next
/// fewest people is hidden too, because the hidden one could otherwise be recovered by subtracting the others from the
/// total. When there is no such cell, every cell is hidden: the visible zeros would place everyone in the hidden cell.
/// </summary>
public static class Suppression
{
    /// <param name="counts">What each cell would show.</param>
    /// <param name="members">The distinct members each cell covers, cell by cell.</param>
    /// <param name="minimum">The smallest number of people a shown cell may cover.</param>
    public static IReadOnlyList<bool> Apply(IReadOnlyList<int> counts, IReadOnlyList<int> members, int minimum)
    {
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count != counts.Count)
        {
            throw new ArgumentException("Every cell needs its member count.", nameof(members));
        }

        var hidden = counts.Select((count, index) => count > 0 && members[index] < minimum).ToArray();
        if (hidden.Count(isHidden => isHidden) == 1)
        {
            var complement = Enumerable.Range(0, counts.Count)
                .Where(index => !hidden[index] && counts[index] > 0)
                .OrderBy(index => members[index])
                .ThenBy(index => counts[index])
                .ThenBy(index => index)
                .Select(index => (int?)index)
                .FirstOrDefault();
            if (complement is { } index)
            {
                hidden[index] = true;
            }
            else
            {
                Array.Fill(hidden, true);
            }
        }

        return hidden;
    }
}
