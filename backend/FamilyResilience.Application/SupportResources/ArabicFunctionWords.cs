namespace FamilyResilience.Application.SupportResources;

/// <summary>Arabic words that carry no search meaning, written in their folded spelling (see the query text rules).</summary>
internal static class ArabicFunctionWords
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "في", "من", "علي", "عن", "الي", "او", "ان", "انا", "انت", "انتم", "هو", "هي", "هم", "نحن", "هل", "كيف",
        "ماذا", "ما", "لماذا", "متي", "اين", "مع", "عند", "عندي", "عندنا", "لدي", "لدينا", "لي", "لنا", "له",
        "لها", "لهم", "به", "بها", "فيه", "فيها", "منه", "منها", "عليه", "عليها", "كل", "هذا", "هذه", "ذلك",
        "تلك", "الذي", "التي", "الذين", "اللي", "يا", "لا", "نعم", "بعد", "قبل", "حتي", "ثم", "ام", "بين",
        "لكن", "اذا", "قد", "لقد", "كان", "كانت", "يكون", "شي", "شيء", "ابي", "ابغي", "اريد", "ودي", "ممكن",
        "شلون", "وش", "ايش", "ابا", "احتاج",

        // Added after the synthetic evaluation (Task 9). Existence, quantity, frequency and cause:
        "يوجد", "توجد", "هناك", "كثير", "كثيرا", "جدا", "اكثر", "اقل", "فقط", "دائما", "بسبب", "لان", "لانه",

        // A function word with "and" attached is still a function word:
        "وانا", "وانت", "وما", "ولا", "وهو", "وهي", "وفي", "ومن", "واذا", "ولكن",

        // Gulf dialect:
        "وايد", "دايم", "دايما", "بس", "عشان", "علشان", "وين", "ليش", "شنو", "مب", "مو",
    };
}
