namespace FamilyResilience.Application.Assistant;

/// <summary>
/// The approved graph of questions, needs, services, authorities and emirates (D-115). It holds public facts only, and
/// inference over it is deterministic, so the same answers always reach the same services.
/// </summary>
public interface ISupportKnowledgeGraph
{
    /// <summary>Every authority in the file's order, past its review date or not.</summary>
    IReadOnlyList<SupportAuthority> Authorities { get; }

    /// <summary>The authority with this code, or null.</summary>
    SupportAuthority? FindAuthority(string code);

    /// <summary>Authorities a family can read about today: those past their review date have left.</summary>
    IReadOnlyList<SupportAuthority> Current(DateOnly today);

    /// <summary>
    /// Walks from each answer through its question's needs to the services addressing them, the most room to improve first
    /// and the file's order after that. An answer at the top of its scale, or outside the scale, leads nowhere, and so does
    /// an authority past its review date.
    /// </summary>
    IReadOnlyList<SupportCandidate> Infer(IReadOnlyList<ScoreReportAnswer> answers, DateOnly today, int limit);
}
