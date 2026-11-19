using System.Collections.Frozen;

namespace FamilyResilience.Application.SupportResources;

/// <summary>
/// PostgreSQL's English stop words (the <c>english</c> configuration's list), dropped while parsing so they never use up
/// the eight-term budget. PostgreSQL would ignore them anyway; an integration test checks that every word here is one
/// PostgreSQL drops, so removing them early never changes what is searched. Public (and frozen) only so that test can
/// read this list rather than a copy of it.
/// </summary>
public static class EnglishStopWords
{
    public static readonly IReadOnlySet<string> All = new[]
    {
        "i", "me", "my", "myself", "we", "our", "ours", "ourselves", "you", "your", "yours", "yourself", "yourselves",
        "he", "him", "his", "himself", "she", "her", "hers", "herself", "it", "its", "itself", "they", "them", "their",
        "theirs", "themselves", "what", "which", "who", "whom", "this", "that", "these", "those", "am", "is", "are",
        "was", "were", "be", "been", "being", "have", "has", "had", "having", "do", "does", "did", "doing", "a", "an",
        "the", "and", "but", "if", "or", "because", "as", "until", "while", "of", "at", "by", "for", "with", "about",
        "against", "between", "into", "through", "during", "before", "after", "above", "below", "to", "from", "up",
        "down", "in", "out", "on", "off", "over", "under", "again", "further", "then", "once", "here", "there", "when",
        "where", "why", "how", "all", "any", "both", "each", "few", "more", "most", "other", "some", "such", "no", "nor",
        "not", "only", "own", "same", "so", "than", "too", "very", "s", "t", "can", "will", "just", "don", "should",
        "now",
    }.ToFrozenSet(StringComparer.Ordinal);
}
